using Controledu.Common.Runtime;
using Controledu.Detection.Abstractions;
using Controledu.Student.Agent.Models;
using Controledu.Student.Agent.Options;
using Controledu.Transport.Dto;
using Controledu.Storage.Stores;
using System.Globalization;

namespace Controledu.Student.Agent.Services;

/// <summary>
/// Handles student sync loops: self-test/hand-raise, diagnostics, accessibility,
/// chat and teacher TTS command delivery.
/// </summary>
public interface ISyncChatCycleService
{
    /// <summary>
    /// Runs one sync pass.
    /// </summary>
    Task RunAsync(
        ResolvedStudentBinding binding,
        string deviceDisplayName,
        StudentTeacherTtsOptions ttsOptions,
        int handRaiseCooldownSeconds,
        bool runChatSync,
        CancellationToken cancellationToken);
}

internal sealed class SyncChatCycleService(
    StudentHubClient hubClient,
    IStudentLocalHostClient studentLocalHostClient,
    DatasetCollectionService datasetCollectionService,
    DiagnosticsExportUploader diagnosticsExportUploader,
    ITeacherTtsSynthesisService teacherTtsSynthesisService,
    ITeacherTtsPlaybackQueue teacherTtsPlaybackQueue,
    ISettingsStore settingsStore,
    ILogger<SyncChatCycleService> logger) : ISyncChatCycleService
{
    public async Task RunAsync(
        ResolvedStudentBinding binding,
        string deviceDisplayName,
        StudentTeacherTtsOptions ttsOptions,
        int handRaiseCooldownSeconds,
        bool runChatSync,
        CancellationToken cancellationToken)
    {
        await HandleSelfTestRequestAsync(binding, deviceDisplayName, cancellationToken);
        await HandleHandRaiseRequestAsync(binding, deviceDisplayName, handRaiseCooldownSeconds, cancellationToken);
        await HandleDiagnosticsExportRequestsAsync(binding, cancellationToken);
        await HandleAccessibilityProfileCommandsAsync(binding, cancellationToken);
        await HandleTeacherLiveCaptionCommandsAsync(binding, cancellationToken);

        if (runChatSync)
        {
            await HandleTeacherChatCommandsAsync(binding, cancellationToken);
            await HandleStudentChatOutboxAsync(binding, cancellationToken);
        }

        await HandleTeacherTtsCommandsAsync(binding, ttsOptions, cancellationToken);
    }

    private async Task HandleSelfTestRequestAsync(
        ResolvedStudentBinding binding,
        string deviceDisplayName,
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

    private async Task HandleDiagnosticsExportRequestsAsync(ResolvedStudentBinding binding, CancellationToken cancellationToken)
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

    private async Task HandleAccessibilityProfileCommandsAsync(ResolvedStudentBinding binding, CancellationToken cancellationToken)
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

    private async Task HandleTeacherLiveCaptionCommandsAsync(ResolvedStudentBinding binding, CancellationToken cancellationToken)
    {
        while (hubClient.TryDequeueTeacherLiveCaption(out var caption))
        {
            if (!string.Equals(caption.ClientId, binding.ClientId, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "Ignoring live caption command for client {CommandClientId}; current binding is {BindingClientId}",
                    caption.ClientId,
                    binding.ClientId);
                continue;
            }

            var delivered = await studentLocalHostClient.TryDeliverTeacherLiveCaptionAsync(caption, cancellationToken);
            if (!delivered)
            {
                await settingsStore.SetAsync(DetectionSettingKeys.LastAlert, "Teacher live caption delivery failed", cancellationToken);
            }
        }
    }

    private async Task HandleTeacherChatCommandsAsync(ResolvedStudentBinding binding, CancellationToken cancellationToken)
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

    private async Task HandleStudentChatOutboxAsync(ResolvedStudentBinding binding, CancellationToken cancellationToken)
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
        StudentTeacherTtsOptions ttsOptions,
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

            byte[]? audioBytes = null;
            if (!string.IsNullOrWhiteSpace(command.AudioWavBase64))
            {
                try
                {
                    audioBytes = Convert.FromBase64String(command.AudioWavBase64);
                }
                catch (FormatException ex)
                {
                    logger.LogWarning(ex, "Teacher TTS command contained invalid pre-synthesized audio payload.");
                    await settingsStore.SetAsync(DetectionSettingKeys.LastAlert, "Teacher TTS audio payload invalid", cancellationToken);
                    continue;
                }
            }
            else
            {
                if (!ttsOptions.Enabled)
                {
                    await settingsStore.SetAsync(DetectionSettingKeys.LastAlert, "Teacher TTS command ignored: TTS disabled", cancellationToken);
                    continue;
                }

                audioBytes = await teacherTtsSynthesisService.TrySynthesizeAsync(command, cancellationToken);
                if (audioBytes is null || audioBytes.Length == 0)
                {
                    await settingsStore.SetAsync(DetectionSettingKeys.LastAlert, "Teacher TTS synthesis failed", cancellationToken);
                    continue;
                }
            }

            var requestId = string.IsNullOrWhiteSpace(command.RequestId) ? Guid.NewGuid().ToString("N") : command.RequestId;
            await teacherTtsPlaybackQueue.QueueAsync(
                new QueuedTeacherTtsAudio(audioBytes, command.MessageText, command.TeacherDisplayName, requestId),
                cancellationToken);

            await settingsStore.SetAsync(
                DetectionSettingKeys.LastAlert,
                $"Teacher TTS queued ({Math.Min(command.MessageText.Length, 40)} chars)",
                cancellationToken);
        }
    }
}
