using Controledu.Common.Runtime;
using Controledu.Detection.Abstractions;
using Controledu.Detection.Core;
using Controledu.Student.Agent.Models;
using Controledu.Transport.Dto;
using Controledu.Storage.Stores;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text.Json;

namespace Controledu.Student.Agent.Services;

/// <summary>
/// Handles detection policy resolution and one AI detection cycle.
/// </summary>
public interface IDetectionCycleService
{
    /// <summary>
    /// Resolves effective policy from local defaults, hub policy and local override.
    /// </summary>
    Task<DetectionPolicyDto> ResolveEffectivePolicyAsync(DetectionPolicyDto localDefaults, CancellationToken cancellationToken);

    /// <summary>
    /// Runs one detection cycle and publishes alert when needed.
    /// </summary>
    Task RunAsync(
        ResolvedStudentBinding binding,
        string deviceDisplayName,
        ScreenCaptureResult? latestCapture,
        DetectionPolicyDto policy,
        CancellationToken cancellationToken);
}

internal sealed class DetectionCycleService(
    IActiveWindowProvider activeWindowProvider,
    DetectionPipeline detectionPipeline,
    DatasetCollectionService datasetCollectionService,
    StudentHubClient hubClient,
    ISettingsStore settingsStore,
    ILogger<DetectionCycleService> logger) : IDetectionCycleService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DetectionPolicyDto> ResolveEffectivePolicyAsync(DetectionPolicyDto localDefaults, CancellationToken cancellationToken)
    {
        var effectivePolicy = localDefaults;
        if (hubClient.TryGetLatestDetectionPolicy(out var hubPolicy) && hubPolicy is not null)
        {
            effectivePolicy = hubPolicy;
        }

        var localOverrideRaw = await settingsStore.GetAsync(DetectionSettingKeys.LocalPolicyJson, cancellationToken);
        if (!string.IsNullOrWhiteSpace(localOverrideRaw))
        {
            try
            {
                var localOverride = JsonSerializer.Deserialize<DetectionPolicyDto>(localOverrideRaw, JsonOptions);
                if (localOverride is not null)
                {
                    // Local endpoint override is intentionally limited to enable/disable only.
                    effectivePolicy = effectivePolicy with
                    {
                        Enabled = localOverride.Enabled,
                    };
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Invalid local detection policy override JSON.");
            }
        }

        return effectivePolicy;
    }

    public async Task RunAsync(
        ResolvedStudentBinding binding,
        string deviceDisplayName,
        ScreenCaptureResult? latestCapture,
        DetectionPolicyDto policy,
        CancellationToken cancellationToken)
    {
        var windowObservation = activeWindowProvider.CaptureObservation();
        var observationTimestamp = DateTimeOffset.UtcNow;
        var thumbnail = policy.IncludeAlertThumbnails
            ? BuildThumbnail(latestCapture?.Payload, policy.AlertThumbnailWidth, policy.AlertThumbnailHeight)
            : null;

        var observation = new DetectionObservation
        {
            StudentId = binding.ClientId,
            TimestampUtc = observationTimestamp,
            ActiveProcessName = windowObservation.ActiveProcessName,
            ActiveWindowTitle = windowObservation.ActiveWindowTitle,
            BrowserHintUrl = null,
            FrameBytes = latestCapture?.Payload,
            OptionalThumbnailBytes = thumbnail,
        };

        var settings = new DetectionSettings
        {
            Enabled = policy.Enabled,
            FrameChangeThreshold = policy.FrameChangeThreshold,
            MinRecheckIntervalSeconds = policy.MinRecheckIntervalSeconds,
            MetadataConfidenceThreshold = policy.MetadataThreshold,
            MlConfidenceThreshold = policy.MlThreshold,
            TemporalWindowSize = policy.TemporalWindowSize,
            TemporalRequiredVotes = policy.TemporalRequiredVotes,
            CooldownSeconds = policy.CooldownSeconds,
            Keywords = policy.Keywords,
            WhitelistKeywords = policy.WhitelistKeywords,
        };

        var decision = await detectionPipeline.AnalyzeAsync(observation, settings, cancellationToken);

        await settingsStore.SetAsync(DetectionSettingKeys.LastCheckUtc, observationTimestamp.ToString("O", CultureInfo.InvariantCulture), cancellationToken);
        await settingsStore.SetAsync(DetectionSettingKeys.LastResult, JsonSerializer.Serialize(new
        {
            decision.Result.IsAiUiDetected,
            decision.Result.Confidence,
            Class = decision.Result.Class.ToString(),
            Stage = decision.Result.StageSource.ToString(),
            decision.Result.Reason,
            decision.Result.IsStable,
        }), cancellationToken);
        await settingsStore.SetAsync(DetectionSettingKeys.EffectivePolicyJson, JsonSerializer.Serialize(policy, JsonOptions), cancellationToken);
        await settingsStore.SetAsync(DetectionSettingKeys.DataCollectionEnabled, policy.DataCollectionModeEnabled ? "1" : "0", cancellationToken);

        if (!string.IsNullOrWhiteSpace(decision.Result.ModelVersion))
        {
            await settingsStore.SetAsync(DetectionSettingKeys.LastModelVersion, decision.Result.ModelVersion, cancellationToken);
        }

        await datasetCollectionService.TryCollectAsync(decision.Observation, decision.Result, policy, cancellationToken);

        if (!decision.ShouldEmitAlert || !decision.Result.IsAiUiDetected)
        {
            return;
        }

        var alert = new AlertEventDto(
            StudentId: binding.ClientId,
            StudentDisplayName: deviceDisplayName,
            TimestampUtc: decision.Observation.TimestampUtc,
            DetectionClass: decision.Result.Class,
            Confidence: decision.Result.Confidence,
            Reason: decision.Result.Reason,
            ThumbnailJpegSmall: decision.Observation.OptionalThumbnailBytes,
            ModelVersion: decision.Result.ModelVersion,
            EventId: Guid.NewGuid().ToString("N"),
            StageSource: decision.Result.StageSource.ToString(),
            IsStable: decision.Result.IsStable,
            TriggeredKeywords: decision.Result.TriggeredKeywords?.ToArray());

        await hubClient.SendAlertAsync(alert, cancellationToken);
        await settingsStore.SetAsync(
            DetectionSettingKeys.LastAlert,
            $"{alert.DetectionClass} ({alert.Confidence:F2}) - {alert.Reason}",
            cancellationToken);
    }

    private static byte[]? BuildThumbnail(byte[]? frameBytes, int width, int height)
    {
        if (frameBytes is null || frameBytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var input = new MemoryStream(frameBytes, writable: false);
            using var source = Image.FromStream(input, false, true);
            using var bitmap = new Bitmap(Math.Max(16, width), Math.Max(16, height), PixelFormat.Format24bppRgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.DrawImage(source, new Rectangle(0, 0, bitmap.Width, bitmap.Height));

            using var output = new MemoryStream();
            var jpegEncoder = ImageCodecInfo.GetImageEncoders().First(static codec => codec.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 60L);
            bitmap.Save(output, jpegEncoder, parameters);
            return output.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
