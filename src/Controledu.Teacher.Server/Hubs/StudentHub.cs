using Controledu.Transport.Constants;
using Controledu.Transport.Dto;
using Controledu.Storage.Stores;
using Controledu.Teacher.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace Controledu.Teacher.Server.Hubs;

/// <summary>
/// SignalR hub for student agents.
/// </summary>
public sealed class StudentHub(
    IPairedClientStore pairedClientStore,
    IStudentRegistry studentRegistry,
    ISystemClock clock,
    IDetectionPolicyService detectionPolicyService,
    IDetectionEventStore detectionEventStore,
    IStudentSignalGate studentSignalGate,
    IRemoteControlSessionService remoteControlSessionService,
    IStudentChatService studentChatService,
    IAuditService auditService,
    IFileTransferCoordinator fileTransferCoordinator,
    IHubContext<TeacherHub> teacherHub,
    ILogger<StudentHub> logger) : Hub
{
    private const string ClientIdContextKey = "client-id";
    private static readonly Action<ILogger, string, Exception?> LogRejectedRegistration =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(3001, nameof(LogRejectedRegistration)), "Rejected student registration for {ClientId}");
    private static readonly Action<ILogger, string, Exception?> LogRejectedUnauthenticatedHubCall =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(3002, nameof(LogRejectedUnauthenticatedHubCall)), "Rejected hub call from unauthenticated connection {ConnectionId}");
    private static readonly Action<ILogger, string, string, Exception?> LogRejectedClientIdMismatch =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(3003, nameof(LogRejectedClientIdMismatch)),
            "Rejected hub call: clientId mismatch. Registered={RegisteredClientId}, payload={PayloadClientId}");
    private static readonly Action<ILogger, string, string, Exception?> LogRejectedMissingRegistrySession =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(3004, nameof(LogRejectedMissingRegistrySession)),
            "Rejected hub call: missing active registry session for clientId={ClientId}, connectionId={ConnectionId}");
    private static readonly TimeSpan StudentSignalCooldown = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Registers student session.
    /// </summary>
    public async Task<StudentRegisterResultDto> Register(StudentRegistrationDto registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var validToken = await pairedClientStore.ValidateTokenAsync(registration.ClientId, registration.Token, Context.ConnectionAborted);
        if (!validToken)
        {
            LogRejectedRegistration(logger, registration.ClientId, null);
            return new StudentRegisterResultDto(false, "Invalid token.", null);
        }

        var student = studentRegistry.Upsert(Context.ConnectionId, registration, clock.UtcNow);
        Context.Items[ClientIdContextKey] = registration.ClientId;

        await teacherHub.Clients.All.SendAsync(HubMethods.StudentUpserted, student, Context.ConnectionAborted);
        await teacherHub.Clients.All.SendAsync(HubMethods.StudentListChanged, studentRegistry.GetAll(), Context.ConnectionAborted);
        await auditService.RecordAsync("device_connected", registration.ClientId, $"{registration.HostName} ({registration.UserName})", Context.ConnectionAborted);

        var policy = await detectionPolicyService.GetAsync(Context.ConnectionAborted);
        studentRegistry.SetDetectionEnabledForAll(policy.Enabled);
        await teacherHub.Clients.All.SendAsync(HubMethods.StudentListChanged, studentRegistry.GetAll(), Context.ConnectionAborted);
        await Clients.Caller.SendAsync(HubMethods.DetectionPolicyUpdated, policy, Context.ConnectionAborted);

        return new StudentRegisterResultDto(true, "Registered", student.HostName);
    }

    /// <summary>
    /// Receives a screen frame from student.
    /// </summary>
    public Task SendFrame(ScreenFrameDto frame)
    {
        if (!IsAuthorized(frame.ClientId))
        {
            return Task.CompletedTask;
        }

        return teacherHub.Clients.All.SendAsync(HubMethods.FrameReceived, frame, Context.ConnectionAborted);
    }

    /// <summary>
    /// Receives heartbeat update.
    /// </summary>
    public async Task Heartbeat(HeartbeatDto heartbeat)
    {
        if (!IsAuthorized(heartbeat.ClientId))
        {
            return;
        }

        var student = studentRegistry.Heartbeat(heartbeat.ClientId, clock.UtcNow);
        if (student is not null)
        {
            await teacherHub.Clients.All.SendAsync(HubMethods.StudentUpserted, student, Context.ConnectionAborted);
        }
    }

    /// <summary>
    /// Receives detector alert.
    /// </summary>
    public async Task SendAlert(AlertEventDto alert)
    {
        if (!IsAuthorized(alert.StudentId))
        {
            return;
        }

        detectionEventStore.Add(alert);
        var updated = studentRegistry.UpdateDetectionAlert(alert);
        if (updated is not null)
        {
            await teacherHub.Clients.All.SendAsync(HubMethods.StudentUpserted, updated, Context.ConnectionAborted);
        }

        await teacherHub.Clients.All.SendAsync(HubMethods.AlertReceived, alert, Context.ConnectionAborted);
        await auditService.RecordAsync(
            "detection_alert",
            alert.StudentId,
            $"{alert.DetectionClass} {alert.Confidence:F2}: {alert.Reason}",
            Context.ConnectionAborted);
    }

    /// <summary>
    /// Receives non-detection student signal (for example hand raise).
    /// </summary>
    public async Task SendStudentSignal(StudentSignalEventDto signal)
    {
        if (!IsAuthorized(signal.StudentId))
        {
            return;
        }

        if (!studentSignalGate.ShouldForward(signal.StudentId, signal.SignalType, clock.UtcNow, StudentSignalCooldown))
        {
            return;
        }

        await teacherHub.Clients.All.SendAsync(HubMethods.StudentSignalReceived, signal, Context.ConnectionAborted);
        await auditService.RecordAsync(
            "student_signal",
            signal.StudentId,
            $"{signal.SignalType}: {signal.Message}",
            Context.ConnectionAborted);
    }

    /// <summary>
    /// Receives chat message from student endpoint overlay via Student.Agent.
    /// </summary>
    public async Task SendChatMessage(StudentTeacherChatMessageDto message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!IsAuthorized(message.ClientId))
        {
            return;
        }

        var student = studentRegistry.GetAll().FirstOrDefault(x => string.Equals(x.ClientId, message.ClientId, StringComparison.Ordinal));
        var normalized = studentChatService.Add(message with
        {
            SenderRole = "student",
            SenderDisplayName = string.IsNullOrWhiteSpace(student?.HostName) ? message.ClientId : student!.HostName,
            TimestampUtc = message.TimestampUtc == default ? clock.UtcNow : message.TimestampUtc,
        });

        await teacherHub.Clients.All.SendAsync(HubMethods.ChatMessageReceived, normalized, Context.ConnectionAborted);
        await auditService.RecordAsync("student_chat_message", normalized.ClientId, $"len={normalized.Text.Length}", Context.ConnectionAborted);
    }

    /// <summary>
    /// Receives file transfer progress from student.
    /// </summary>
    public async Task ReportFileProgress(FileDeliveryProgressDto progress)
    {
        if (!IsAuthorized(progress.ClientId))
        {
            return;
        }

        fileTransferCoordinator.UpdateProgress(progress);
        await teacherHub.Clients.All.SendAsync(HubMethods.FileProgressUpdated, progress, Context.ConnectionAborted);

        if (progress.Completed)
        {
            await auditService.RecordAsync("file_transfer_completed", progress.ClientId, $"Transfer {progress.TransferId} completed", Context.ConnectionAborted);
        }
        else if (!string.IsNullOrWhiteSpace(progress.Error))
        {
            await auditService.RecordAsync("file_transfer_error", progress.ClientId, $"Transfer {progress.TransferId}: {progress.Error}", Context.ConnectionAborted);
        }
    }

    /// <summary>
    /// Receives remote control session state from student agent.
    /// </summary>
    public async Task ReportRemoteControlStatus(RemoteControlSessionStatusDto status)
    {
        if (!IsAuthorized(status.StudentId))
        {
            return;
        }

        _ = remoteControlSessionService.TryApplyStudentStatus(status, out _);
        await teacherHub.Clients.All.SendAsync(HubMethods.RemoteControlStatusUpdated, status, Context.ConnectionAborted);
        await auditService.RecordAsync(
            "remote_control_status",
            status.StudentId,
            $"{status.State}: {status.Message ?? "-"}",
            Context.ConnectionAborted);
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var student = studentRegistry.Disconnect(Context.ConnectionId, clock.UtcNow);
        if (student is not null)
        {
            await teacherHub.Clients.All.SendAsync(HubMethods.StudentDisconnected, student.ClientId, Context.ConnectionAborted);
            await teacherHub.Clients.All.SendAsync(HubMethods.StudentListChanged, studentRegistry.GetAll(), Context.ConnectionAborted);
            await auditService.RecordAsync("device_disconnected", student.ClientId, student.HostName, CancellationToken.None);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Returns current runtime detection policy for connected student agent.
    /// </summary>
    public Task<DetectionPolicyDto> GetDetectionPolicy() => detectionPolicyService.GetAsync(Context.ConnectionAborted);

    private bool IsAuthorized(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return false;
        }

        if (!Context.Items.TryGetValue(ClientIdContextKey, out var value) || value is not string registeredClientId)
        {
            LogRejectedUnauthenticatedHubCall(logger, Context.ConnectionId, null);
            return false;
        }

        if (!string.Equals(registeredClientId, clientId, StringComparison.Ordinal))
        {
            LogRejectedClientIdMismatch(logger, registeredClientId, clientId, null);
            return false;
        }

        if (!studentRegistry.TryGetConnectionId(registeredClientId, out var activeConnectionId)
            || !string.Equals(activeConnectionId, Context.ConnectionId, StringComparison.Ordinal))
        {
            LogRejectedMissingRegistrySession(logger, registeredClientId, Context.ConnectionId, null);
            return false;
        }

        return true;
    }
}

