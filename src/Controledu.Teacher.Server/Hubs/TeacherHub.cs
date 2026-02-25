using Controledu.Common.Runtime;
using Controledu.Transport.Constants;
using Controledu.Transport.Dto;
using Controledu.Storage.Models;
using Controledu.Storage.Stores;
using Controledu.Teacher.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace Controledu.Teacher.Server.Hubs;

/// <summary>
/// SignalR hub for teacher UI clients.
/// </summary>
public sealed class TeacherHub(
    IStudentRegistry studentRegistry,
    IPairingCodeService pairingCodeService,
    IAuditLogStore auditLogStore,
    IHubContext<StudentHub> studentHub,
    IRemoteControlSessionService remoteControlSessionService,
    ISystemClock clock,
    IAuditService auditService,
    ILogger<TeacherHub> logger) : Hub
{
    /// <summary>
    /// Returns active and known student list.
    /// </summary>
    public Task<IReadOnlyList<StudentInfoDto>> GetStudents() => Task.FromResult(studentRegistry.GetAll());

    /// <summary>
    /// Generates one-time pairing PIN.
    /// </summary>
    public Task<PairingPinDto> GeneratePairingPin() => Task.FromResult(pairingCodeService.Generate());

    /// <summary>
    /// Returns latest audit entries.
    /// </summary>
    public Task<IReadOnlyList<AuditLogModel>> GetLatestAudit(int take = 50) =>
        auditLogStore.GetLatestAsync(Math.Clamp(take, 1, 500), Context.ConnectionAborted);

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync(HubMethods.StudentListChanged, studentRegistry.GetAll(), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Requests remote control session for a student.
    /// </summary>
    public async Task<RemoteControlSessionStartResultDto> RequestRemoteControlSession(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return new RemoteControlSessionStartResultDto(false, null, "Client id is required.");
        }

        if (!studentRegistry.TryGetConnectionId(clientId, out var studentConnectionId) || string.IsNullOrWhiteSpace(studentConnectionId))
        {
            return new RemoteControlSessionStartResultDto(false, null, "Student is offline.");
        }

        var lease = remoteControlSessionService.Start(clientId, Context.ConnectionId, clock.UtcNow);
        var command = new RemoteControlSessionCommandDto(
            clientId,
            lease.SessionId,
            RemoteControlSessionAction.RequestStart,
            lease.CreatedAtUtc,
            RequestedBy: Context.ConnectionId,
            ApprovalTimeoutSeconds: 20,
            MaxSessionSeconds: 600);

        await studentHub.Clients.Client(studentConnectionId).SendAsync(HubMethods.RemoteControlSessionCommand, command, Context.ConnectionAborted);
        await auditService.RecordAsync("remote_control_requested", clientId, $"Teacher={Context.ConnectionId}", Context.ConnectionAborted);
        logger.LogInformation("Remote control requested for {ClientId}, session {SessionId}", clientId, lease.SessionId);
        return new RemoteControlSessionStartResultDto(true, lease.SessionId, "Remote control request sent.");
    }

    /// <summary>
    /// Stops active remote control session for a student.
    /// </summary>
    public async Task StopRemoteControlSession(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }

        if (!remoteControlSessionService.TryStop(clientId, Context.ConnectionId, clock.UtcNow, out var lease) || lease is null)
        {
            return;
        }

        if (studentRegistry.TryGetConnectionId(clientId, out var studentConnectionId) && !string.IsNullOrWhiteSpace(studentConnectionId))
        {
            var command = new RemoteControlSessionCommandDto(
                clientId,
                lease.SessionId,
                RemoteControlSessionAction.Stop,
                clock.UtcNow,
                RequestedBy: Context.ConnectionId);
            await studentHub.Clients.Client(studentConnectionId).SendAsync(HubMethods.RemoteControlSessionCommand, command, Context.ConnectionAborted);
        }

        await Clients.All.SendAsync(
            HubMethods.RemoteControlStatusUpdated,
            new RemoteControlSessionStatusDto(clientId, clientId, lease.SessionId, RemoteControlSessionState.Ended, clock.UtcNow, "Stopped by teacher."),
            Context.ConnectionAborted);
        await auditService.RecordAsync("remote_control_stopped", clientId, $"Teacher={Context.ConnectionId}", Context.ConnectionAborted);
    }

    /// <summary>
    /// Relays remote input command to approved student session.
    /// </summary>
    public async Task SendRemoteControlInput(RemoteControlInputCommandDto command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!remoteControlSessionService.CanForwardInput(command, Context.ConnectionId))
        {
            return;
        }

        if (!studentRegistry.TryGetConnectionId(command.ClientId, out var studentConnectionId) || string.IsNullOrWhiteSpace(studentConnectionId))
        {
            return;
        }

        await studentHub.Clients.Client(studentConnectionId).SendAsync(HubMethods.RemoteControlInputCommand, command, Context.ConnectionAborted);
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var removed = remoteControlSessionService.RemoveTeacherSessions(Context.ConnectionId, clock.UtcNow);
        foreach (var lease in removed)
        {
            if (studentRegistry.TryGetConnectionId(lease.ClientId, out var studentConnectionId) && !string.IsNullOrWhiteSpace(studentConnectionId))
            {
                var command = new RemoteControlSessionCommandDto(
                    lease.ClientId,
                    lease.SessionId,
                    RemoteControlSessionAction.Stop,
                    clock.UtcNow,
                    RequestedBy: Context.ConnectionId);

                await studentHub.Clients.Client(studentConnectionId).SendAsync(HubMethods.RemoteControlSessionCommand, command, CancellationToken.None);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }
}

