using Controledu.Common.Models;
using Controledu.Common.Runtime;
using Controledu.Student.Agent.Models;
using Controledu.Student.Agent.Options;
using Controledu.Student.Agent.Services;
using Controledu.Storage.Stores;
using Microsoft.Extensions.Options;
using Controledu.Transport.Dto;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Controledu.Student.Agent;

/// <summary>
/// Main student background worker orchestrating independent domain cycles.
/// </summary>
public sealed class Worker(
    IOptions<StudentAgentOptions> options,
    IBindingResolver bindingResolver,
    StudentHubClient hubClient,
    ICaptureCycleService captureCycleService,
    IDetectionCycleService detectionCycleService,
    ISyncChatCycleService syncChatCycleService,
    IFileTransferCycleService fileTransferCycleService,
    IRemoteControlCycleService remoteControlCycleService,
    IStorageInitializer storageInitializer,
    IStudentBindingStore bindingStore,
    ISettingsStore settingsStore,
    ILogger<Worker> logger) : BackgroundService
{
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
                if (connectResult == StudentConnectResult.ForceUnpair)
                {
                    var reason = hubClient.ConsumeForceUnpairReason() ?? "Pairing revoked by server.";
                    await HandleForcedUnpairAsync(reason, stoppingToken);
                    await hubClient.DisconnectAsync(stoppingToken);
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                if (connectResult != StudentConnectResult.Connected)
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
                    var captureOutcome = await captureCycleService.RunAsync(binding, nextSequence, fps, jpegQuality, stoppingToken);
                    latestCapture = captureOutcome.Capture ?? latestCapture;

                    ApplyAdaptiveCaptureSettings(configuration, captureOutcome.Duration, ref fps, ref jpegQuality);
                    nextCaptureAt = DateTimeOffset.UtcNow.AddSeconds(1.0 / Math.Max(1, fps));
                }

                var effectivePolicy = await detectionCycleService.ResolveEffectivePolicyAsync(configuration.Detection, stoppingToken);

                if (now >= nextDetectorAt)
                {
                    await detectionCycleService.RunAsync(binding, currentDeviceName, latestCapture, effectivePolicy, stoppingToken);
                    nextDetectorAt = now.AddSeconds(Math.Max(1, effectivePolicy.EvaluationIntervalSeconds));
                }

                var runChatSync = now >= nextChatSyncAt;
                await syncChatCycleService.RunAsync(
                    binding,
                    currentDeviceName,
                    configuration.TeacherTts,
                    configuration.HandRaiseCooldownSeconds,
                    runChatSync,
                    stoppingToken);
                if (runChatSync)
                {
                    nextChatSyncAt = now.AddMilliseconds(450);
                }

                await remoteControlCycleService.RunAsync(binding, currentDeviceName, stoppingToken);
                await fileTransferCycleService.RunAsync(binding, stoppingToken);

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

    private static void ApplyAdaptiveCaptureSettings(
        StudentAgentOptions configuration,
        TimeSpan captureDuration,
        ref int fps,
        ref int jpegQuality)
    {
        if (captureDuration > TimeSpan.FromMilliseconds(220))
        {
            fps = Math.Max(configuration.MinFps, fps - 2);
            jpegQuality = Math.Max(configuration.MinJpegQuality, jpegQuality - 6);
            return;
        }

        if (captureDuration > TimeSpan.FromMilliseconds(140))
        {
            fps = Math.Max(configuration.MinFps, fps - 1);
            jpegQuality = Math.Max(configuration.MinJpegQuality, jpegQuality - 3);
            return;
        }

        if (captureDuration > TimeSpan.Zero && captureDuration < TimeSpan.FromMilliseconds(55))
        {
            fps = Math.Min(configuration.MaxFps, fps + 1);
            jpegQuality = Math.Min(configuration.MaxJpegQuality, jpegQuality + 1);
        }
    }

    private async Task HandleForcedUnpairAsync(string reason, CancellationToken cancellationToken)
    {
        await bindingStore.ClearAsync(cancellationToken);
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
}
