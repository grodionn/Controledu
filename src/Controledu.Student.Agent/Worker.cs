using Controledu.Common.Runtime;
using Controledu.Common.Models;
using Controledu.Detection.Abstractions;
using Controledu.Detection.Core;
using Controledu.Transport.Dto;
using Controledu.Student.Agent.Models;
using Controledu.Student.Agent.Options;
using Microsoft.Extensions.Options;
using Controledu.Storage.Stores;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Controledu.Student.Agent;

/// <summary>
/// Main student background worker handling reconnect, capture, alerts, file transfers, and AI detection.
/// </summary>
public sealed class Worker(
    IOptions<StudentAgentOptions> options,
    Services.IBindingResolver bindingResolver,
    Services.StudentHubClient hubClient,
    Services.IScreenCaptureService screenCaptureService,
    Services.IActiveWindowProvider activeWindowProvider,
    DetectionPipeline detectionPipeline,
    Services.DatasetCollectionService datasetCollectionService,
    Services.DiagnosticsExportUploader diagnosticsExportUploader,
    Services.FileTransferReceiver fileTransferReceiver,
    Services.IStudentLocalHostClient studentLocalHostClient,
    Services.ITeacherTtsSynthesisService teacherTtsSynthesisService,
    Services.ITeacherTtsPlaybackQueue teacherTtsPlaybackQueue,
    Services.IRemoteControlService remoteControlService,
    Controledu.Storage.Stores.IStorageInitializer storageInitializer,
    IStudentBindingStore bindingStore,
    ISettingsStore settingsStore,
    ILogger<Worker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await storageInitializer.EnsureCreatedAsync(stoppingToken);

        var configuration = options.Value;
        var fps = Math.Clamp(configuration.InitialFps, configuration.MinFps, configuration.MaxFps);
        var jpegQuality = Math.Clamp(configuration.InitialJpegQuality, configuration.MinJpegQuality, configuration.MaxJpegQuality);
        var sequence = 0;
        var currentDeviceName = Environment.MachineName;
        ScreenCaptureResult? latestCapture = null;

        var nextHeartbeatAt = DateTimeOffset.MinValue;
        var nextCaptureAt = DateTimeOffset.MinValue;
        var nextDetectorAt = DateTimeOffset.MinValue;
        var nextIdentityRefreshAt = DateTimeOffset.MinValue;
        var nextChatSyncAt = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var binding = await bindingResolver.GetAsync(stoppingToken);
                if (binding is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                if (DateTimeOffset.UtcNow >= nextIdentityRefreshAt)
                {
                    currentDeviceName = await ResolveConfiguredDeviceNameAsync(stoppingToken);
                    nextIdentityRefreshAt = DateTimeOffset.UtcNow.AddSeconds(15);
                }

                var identity = BuildIdentity(currentDeviceName);
                var connectResult = await hubClient.EnsureConnectedAsync(binding, identity, stoppingToken);
                if (connectResult == Services.StudentConnectResult.ForceUnpair)
                {
                    var reason = hubClient.ConsumeForceUnpairReason() ?? "Pairing revoked by server.";
                    await HandleForcedUnpairAsync(reason, bindingStore, stoppingToken);
                    await hubClient.DisconnectAsync(stoppingToken);
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                if (connectResult != Services.StudentConnectResult.Connected)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                    continue;
                }

                var now = DateTimeOffset.UtcNow;

                if (now >= nextHeartbeatAt)
                {
                    await hubClient.SendHeartbeatAsync(new HeartbeatDto(binding.ClientId, now), stoppingToken);
                    nextHeartbeatAt = now.AddSeconds(Math.Max(2, configuration.HeartbeatIntervalSeconds));
                }

                if (now >= nextCaptureAt)
                {
                    var nextSequence = Interlocked.Increment(ref sequence);
                    var captureOutcome = await TryCaptureAndSendAsync(binding, fps, jpegQuality, nextSequence, stoppingToken);
                    latestCapture = captureOutcome.Capture ?? latestCapture;

                    if (captureOutcome.Duration > TimeSpan.FromMilliseconds(220))
                    {
                        fps = Math.Max(configuration.MinFps, fps - 2);
                        jpegQuality = Math.Max(configuration.MinJpegQuality, jpegQuality - 6);
                    }
                    else if (captureOutcome.Duration > TimeSpan.FromMilliseconds(140))
                    {
                        fps = Math.Max(configuration.MinFps, fps - 1);
                        jpegQuality = Math.Max(configuration.MinJpegQuality, jpegQuality - 3);
                    }
                    else if (captureOutcome.Duration > TimeSpan.Zero && captureOutcome.Duration < TimeSpan.FromMilliseconds(55))
                    {
                        fps = Math.Min(configuration.MaxFps, fps + 1);
                        jpegQuality = Math.Min(configuration.MaxJpegQuality, jpegQuality + 1);
                    }

                    nextCaptureAt = DateTimeOffset.UtcNow.AddSeconds(1.0 / Math.Max(1, fps));
                }

                var effectivePolicy = await ResolveEffectivePolicyAsync(configuration.Detection, hubClient, settingsStore, stoppingToken);

                if (now >= nextDetectorAt)
                {
                await RunDetectionPipelineAsync(
                    binding,
                    currentDeviceName,
                    latestCapture,
                    activeWindowProvider,
                        detectionPipeline,
                        datasetCollectionService,
                        hubClient,
                        settingsStore,
                        effectivePolicy,
                        stoppingToken);

                    nextDetectorAt = now.AddSeconds(Math.Max(1, effectivePolicy.EvaluationIntervalSeconds));
                }

                await HandleSelfTestRequestAsync(binding, currentDeviceName, hubClient, settingsStore, stoppingToken);
                await HandleHandRaiseRequestAsync(binding, currentDeviceName, hubClient, settingsStore, configuration.HandRaiseCooldownSeconds, stoppingToken);
                await HandleDiagnosticsExportRequestsAsync(binding, hubClient, datasetCollectionService, diagnosticsExportUploader, settingsStore, stoppingToken);
                await HandleAccessibilityProfileCommandsAsync(binding, hubClient, studentLocalHostClient, settingsStore, stoppingToken);
                if (now >= nextChatSyncAt)
                {
                    await HandleTeacherChatCommandsAsync(binding, hubClient, studentLocalHostClient, settingsStore, stoppingToken);
                    await HandleStudentChatOutboxAsync(binding, hubClient, studentLocalHostClient, settingsStore, stoppingToken);
                    nextChatSyncAt = now.AddMilliseconds(450);
                }
                await HandleTeacherTtsCommandsAsync(binding, hubClient, studentLocalHostClient, teacherTtsSynthesisService, teacherTtsPlaybackQueue, configuration.TeacherTts, settingsStore, stoppingToken);
                await remoteControlService.ProcessAsync(binding, currentDeviceName, hubClient, settingsStore, stoppingToken);

                while (hubClient.TryDequeueTransferCommand(out var command))
                {
                    try
                    {
                        await fileTransferReceiver.ProcessAsync(binding, command, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Transfer {TransferId} failed", command.TransferId);
                        await hubClient.SendFileProgressAsync(
                            new FileDeliveryProgressDto(command.TransferId, binding.ClientId, 0, command.TotalChunks, false, ex.Message),
                            stoppingToken);
                    }
                }

                var loopNow = DateTimeOffset.UtcNow;
                var delay = TimeSpan.FromMilliseconds(50);

                if (nextCaptureAt > loopNow)
                {
                    delay = nextCaptureAt - loopNow;
                }

                if (nextDetectorAt > loopNow)
                {
                    var detectorDelay = nextDetectorAt - loopNow;
                    if (detectorDelay < delay)
                    {
                        delay = detectorDelay;
                    }
                }

                if (nextHeartbeatAt > loopNow)
                {
                    var heartbeatDelay = nextHeartbeatAt - loopNow;
                    if (heartbeatDelay < delay)
                    {
                        delay = heartbeatDelay;
                    }
                }

                var boundedDelayMs = Math.Clamp((int)Math.Ceiling(delay.TotalMilliseconds), 1, 50);
                await Task.Delay(TimeSpan.FromMilliseconds(boundedDelayMs), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Student worker loop error");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task<CaptureOutcome> TryCaptureAndSendAsync(
        ResolvedStudentBinding binding,
        int fps,
        int jpegQuality,
        int sequence,
        CancellationToken stoppingToken)
    {
        var started = DateTimeOffset.UtcNow;

        try
        {
            var capture = await screenCaptureService.CaptureAsync(jpegQuality, stoppingToken);
            if (capture is null)
            {
                return new CaptureOutcome(null, TimeSpan.Zero);
            }

            var frame = new ScreenFrameDto(
                binding.ClientId,
                capture.Payload,
                capture.Format,
                capture.Width,
                capture.Height,
                sequence,
                DateTimeOffset.UtcNow);

            await hubClient.SendFrameAsync(frame, stoppingToken);
            return new CaptureOutcome(capture, DateTimeOffset.UtcNow - started);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Frame send failed at FPS {Fps} quality {Quality}", fps, jpegQuality);
            return new CaptureOutcome(null, TimeSpan.FromSeconds(2));
        }
    }

    private async Task RunDetectionPipelineAsync(
        ResolvedStudentBinding binding,
        string deviceDisplayName,
        ScreenCaptureResult? latestCapture,
        Services.IActiveWindowProvider activeWindowProvider,
        DetectionPipeline detectionPipeline,
        Services.DatasetCollectionService datasetCollectionService,
        Services.StudentHubClient hubClient,
        ISettingsStore settingsStore,
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

    private async Task HandleSelfTestRequestAsync(
        ResolvedStudentBinding binding,
        string deviceDisplayName,
        Services.StudentHubClient hubClient,
        ISettingsStore settingsStore,
        CancellationToken cancellationToken)
    {
        var requestFlag = await settingsStore.GetAsync(DetectionSettingKeys.SelfTestRequest, cancellationToken);
        if (!string.Equals(requestFlag, "1", StringComparison.Ordinal))
        {
            return;
        }

        var alert = new AlertEventDto(
            StudentId: binding.ClientId,
            StudentDisplayName: deviceDisplayName,
            TimestampUtc: DateTimeOffset.UtcNow,
            DetectionClass: DetectionClass.UnknownAi,
            Confidence: 1,
            Reason: "Manual detector self-test triggered from local host UI.",
            ThumbnailJpegSmall: null,
            ModelVersion: "self-test",
            EventId: Guid.NewGuid().ToString("N"),
            StageSource: DetectionStageSource.MetadataRule.ToString(),
            IsStable: true,
            TriggeredKeywords: ["self-test"]);

        await hubClient.SendAlertAsync(alert, cancellationToken);
        await settingsStore.SetAsync(DetectionSettingKeys.SelfTestRequest, "0", cancellationToken);
        await settingsStore.SetAsync(DetectionSettingKeys.LastAlert, "Self-test alert sent", cancellationToken);
    }

    private async Task HandleHandRaiseRequestAsync(
        ResolvedStudentBinding binding,
        string deviceDisplayName,
        Services.StudentHubClient hubClient,
        ISettingsStore settingsStore,
        int cooldownSeconds,
        CancellationToken cancellationToken)
    {
        var requestedAtRaw = await settingsStore.GetAsync(DetectionSettingKeys.HandRaiseRequestedAtUtc, cancellationToken);
        if (string.IsNullOrWhiteSpace(requestedAtRaw))
        {
            return;
        }

        if (!DateTimeOffset.TryParse(requestedAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var requestedAtUtc))
        {
            await settingsStore.SetAsync(DetectionSettingKeys.HandRaiseRequestedAtUtc, string.Empty, cancellationToken);
            return;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var cooldown = TimeSpan.FromSeconds(Math.Max(5, cooldownSeconds));
        var lastSentAtRaw = await settingsStore.GetAsync(DetectionSettingKeys.HandRaiseLastSentAtUtc, cancellationToken);
        if (DateTimeOffset.TryParse(lastSentAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lastSentAtUtc))
        {
            if (requestedAtUtc <= lastSentAtUtc)
            {
                await settingsStore.SetAsync(DetectionSettingKeys.HandRaiseRequestedAtUtc, string.Empty, cancellationToken);
                return;
            }

            if (nowUtc - lastSentAtUtc < cooldown)
            {
                return;
            }
        }

        var signal = new StudentSignalEventDto(
            StudentId: binding.ClientId,
            StudentDisplayName: deviceDisplayName,
            SignalType: StudentSignalType.HandRaise,
            TimestampUtc: nowUtc,
            EventId: Guid.NewGuid().ToString("N"),
            Message: "Hand raise requested from endpoint overlay.");

        await hubClient.SendStudentSignalAsync(signal, cancellationToken);
        await settingsStore.SetAsync(DetectionSettingKeys.HandRaiseLastSentAtUtc, nowUtc.ToString("O", CultureInfo.InvariantCulture), cancellationToken);
        await settingsStore.SetAsync(DetectionSettingKeys.HandRaiseRequestedAtUtc, string.Empty, cancellationToken);
        await settingsStore.SetAsync(DetectionSettingKeys.LastAlert, "Hand raise signal sent", cancellationToken);
    }

    private async Task HandleDiagnosticsExportRequestsAsync(
        ResolvedStudentBinding binding,
        Services.StudentHubClient hubClient,
        Services.DatasetCollectionService datasetCollectionService,
        Services.DiagnosticsExportUploader diagnosticsExportUploader,
        ISettingsStore settingsStore,
        CancellationToken cancellationToken)
    {
        while (hubClient.TryDequeueDiagnosticsExportRequest(out var requestId))
        {
            try
            {
                var archivePath = await datasetCollectionService.ExportDatasetAsync(cancellationToken);
                await diagnosticsExportUploader.UploadAsync(binding, archivePath, cancellationToken);

                await settingsStore.SetAsync(
                    DetectionSettingKeys.LastAlert,
                    $"Diagnostics export uploaded ({Path.GetFileName(archivePath)})",
                    cancellationToken);

                logger.LogInformation(
                    "Processed diagnostics export request {RequestId} for client {ClientId}",
                    requestId,
                    binding.ClientId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Diagnostics export request {RequestId} failed for client {ClientId}",
                    requestId,
                    binding.ClientId);

                await settingsStore.SetAsync(
                    DetectionSettingKeys.LastAlert,
                    $"Diagnostics export failed: {ex.Message}",
                    cancellationToken);
            }
        }
    }

    private async Task HandleAccessibilityProfileCommandsAsync(
        ResolvedStudentBinding binding,
        Services.StudentHubClient hubClient,
        Services.IStudentLocalHostClient studentLocalHostClient,
        ISettingsStore settingsStore,
        CancellationToken cancellationToken)
    {
        while (hubClient.TryDequeueAccessibilityProfileCommand(out var command))
        {
            if (!string.Equals(command.ClientId, binding.ClientId, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "Ignoring accessibility profile command for client {CommandClientId}; current binding is {BindingClientId}",
                    command.ClientId,
                    binding.ClientId);
                continue;
            }

            var applied = await studentLocalHostClient.TryApplyTeacherAccessibilityProfileAsync(command, cancellationToken);
            if (applied)
            {
                logger.LogInformation(
                    "Applied teacher accessibility profile for client {ClientId} (preset={Preset})",
                    command.ClientId,
                    command.Profile.ActivePreset);
                await settingsStore.SetAsync(
                    DetectionSettingKeys.LastAlert,
                    $"Accessibility profile applied: {command.Profile.ActivePreset}",
                    cancellationToken);
            }
            else
            {
                await settingsStore.SetAsync(
                    DetectionSettingKeys.LastAlert,
                    $"Accessibility profile apply failed: {command.Profile.ActivePreset}",
                    cancellationToken);
            }
        }
    }

    private async Task HandleTeacherChatCommandsAsync(
        ResolvedStudentBinding binding,
        Services.StudentHubClient hubClient,
        Services.IStudentLocalHostClient studentLocalHostClient,
        ISettingsStore settingsStore,
        CancellationToken cancellationToken)
    {
        while (hubClient.TryDequeueTeacherChatMessage(out var message))
        {
            if (!string.Equals(message.ClientId, binding.ClientId, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "Ignoring teacher chat message for client {CommandClientId}; current binding is {BindingClientId}",
                    message.ClientId,
                    binding.ClientId);
                continue;
            }

            var delivered = await studentLocalHostClient.TryDeliverTeacherChatMessageAsync(message, cancellationToken);
            if (!delivered)
            {
                await settingsStore.SetAsync(DetectionSettingKeys.LastAlert, "Teacher chat message delivery failed", cancellationToken);
                logger.LogDebug("Teacher chat message could not be delivered to Student.Host.");
                continue;
            }

            await settingsStore.SetAsync(DetectionSettingKeys.LastAlert, "Teacher chat message received", cancellationToken);
        }
    }

    private async Task HandleStudentChatOutboxAsync(
        ResolvedStudentBinding binding,
        Services.StudentHubClient hubClient,
        Services.IStudentLocalHostClient studentLocalHostClient,
        ISettingsStore settingsStore,
        CancellationToken cancellationToken)
    {
        var outgoingMessages = await studentLocalHostClient.TryPeekStudentChatOutboxAsync(cancellationToken);
        if (outgoingMessages.Count == 0)
        {
            return;
        }

        var ackIds = new List<string>(outgoingMessages.Count);
        foreach (var message in outgoingMessages)
        {
            if (!string.Equals(message.ClientId, binding.ClientId, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "Skipping local student chat outbox item for {OutboxClientId}; current binding is {BindingClientId}",
                    message.ClientId,
                    binding.ClientId);
                continue;
            }

            try
            {
                await hubClient.SendChatMessageAsync(message with { SenderRole = "student" }, cancellationToken);
                ackIds.Add(message.MessageId);
                await settingsStore.SetAsync(DetectionSettingKeys.LastAlert, "Student chat message sent", cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to forward student chat message {MessageId}", message.MessageId);
                await settingsStore.SetAsync(DetectionSettingKeys.LastAlert, "Student chat message send failed", cancellationToken);
                break;
            }
        }

        if (ackIds.Count > 0)
        {
            var acked = await studentLocalHostClient.TryAckStudentChatOutboxAsync(ackIds, cancellationToken);
            if (!acked)
            {
                logger.LogWarning("Student chat outbox ack failed for {Count} message(s)", ackIds.Count);
                await settingsStore.SetAsync(DetectionSettingKeys.LastAlert, "Student chat ack failed", cancellationToken);
            }
        }
    }

    private async Task HandleTeacherTtsCommandsAsync(
        ResolvedStudentBinding binding,
        Services.StudentHubClient hubClient,
        Services.IStudentLocalHostClient studentLocalHostClient,
        Services.ITeacherTtsSynthesisService teacherTtsSynthesisService,
        Services.ITeacherTtsPlaybackQueue teacherTtsPlaybackQueue,
        StudentTeacherTtsOptions ttsOptions,
        ISettingsStore settingsStore,
        CancellationToken cancellationToken)
    {
        while (hubClient.TryDequeueTeacherTtsCommand(out var command))
        {
            if (!string.Equals(command.ClientId, binding.ClientId, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "Ignoring TTS command for client {CommandClientId}; current binding is {BindingClientId}",
                    command.ClientId,
                    binding.ClientId);
                continue;
            }

            if (!ttsOptions.Enabled)
            {
                await settingsStore.SetAsync(DetectionSettingKeys.LastAlert, "Teacher TTS command ignored: TTS disabled", cancellationToken);
                continue;
            }

            if (ttsOptions.RespectAccessibilityToggle)
            {
                var ttsEnabledByProfile = await studentLocalHostClient.TryGetTeacherTtsEnabledAsync(cancellationToken);
                if (ttsEnabledByProfile is false)
                {
                    logger.LogInformation("Skipping teacher TTS because local accessibility profile disabled TTS.");
                    await settingsStore.SetAsync(DetectionSettingKeys.LastAlert, "Teacher TTS skipped by accessibility profile", cancellationToken);
                    continue;
                }
            }

            var audioBytes = await teacherTtsSynthesisService.TrySynthesizeAsync(command, cancellationToken);
            if (audioBytes is null || audioBytes.Length == 0)
            {
                await settingsStore.SetAsync(DetectionSettingKeys.LastAlert, "Teacher TTS synthesis failed", cancellationToken);
                continue;
            }

            var requestId = string.IsNullOrWhiteSpace(command.RequestId) ? Guid.NewGuid().ToString("N") : command.RequestId;
            await teacherTtsPlaybackQueue.QueueAsync(
                new Services.QueuedTeacherTtsAudio(audioBytes, command.MessageText, command.TeacherDisplayName, requestId),
                cancellationToken);

            await settingsStore.SetAsync(
                DetectionSettingKeys.LastAlert,
                $"Teacher TTS queued ({Math.Min(command.MessageText.Length, 40)} chars)",
                cancellationToken);
        }
    }

    private async Task<DetectionPolicyDto> ResolveEffectivePolicyAsync(
        DetectionPolicyDto localDefaults,
        Services.StudentHubClient hubClient,
        ISettingsStore settingsStore,
        CancellationToken cancellationToken)
    {
        _ = localDefaults;
        _ = hubClient;
        var localOverrideRaw = await settingsStore.GetAsync(DetectionSettingKeys.LocalPolicyJson, cancellationToken);
        if (!string.IsNullOrWhiteSpace(localOverrideRaw))
        {
            try
            {
                _ = JsonSerializer.Deserialize<DetectionPolicyDto>(localOverrideRaw, JsonOptions);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Invalid local detection policy override JSON.");
            }
        }

        return DetectionPolicyFactory.CreateProductionPolicy(enabled: true);
    }

    private async Task HandleForcedUnpairAsync(string reason, IStudentBindingStore studentBindingStore, CancellationToken cancellationToken)
    {
        await studentBindingStore.ClearAsync(cancellationToken);
        await settingsStore.SetAsync(DetectionSettingKeys.LastAlert, $"Pairing revoked: {reason}", cancellationToken);
        logger.LogWarning("Student binding was cleared: {Reason}", reason);
    }

    private async Task<string> ResolveConfiguredDeviceNameAsync(CancellationToken cancellationToken)
    {
        var configuredName = await settingsStore.GetAsync("student.device.display-name", cancellationToken);
        return string.IsNullOrWhiteSpace(configuredName) ? Environment.MachineName : configuredName.Trim();
    }

    private static StudentRuntimeIdentity BuildIdentity(string hostName)
    {
        return new StudentRuntimeIdentity(
            hostName,
            Environment.UserName,
            RuntimeInformation.OSDescription,
            GetLocalIpAddress());
    }

    private static string? GetLocalIpAddress()
    {
        try
        {
            return Dns.GetHostEntry(Dns.GetHostName())
                .AddressList
                .FirstOrDefault(static address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                ?.ToString();
        }
        catch
        {
            return null;
        }
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

    private sealed record CaptureOutcome(ScreenCaptureResult? Capture, TimeSpan Duration);
}

