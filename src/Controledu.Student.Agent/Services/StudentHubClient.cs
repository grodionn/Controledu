using Controledu.Common.Models;
using Controledu.Transport.Constants;
using Controledu.Transport.Dto;
using Controledu.Student.Agent.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace Controledu.Student.Agent.Services;

/// <summary>
/// SignalR client wrapper for student hub communications.
/// </summary>
public sealed class StudentHubClient(ILogger<StudentHubClient> logger) : IAsyncDisposable
{
    private readonly Channel<FileTransferCommandDto> _transferCommands = Channel.CreateUnbounded<FileTransferCommandDto>();
    private readonly Channel<string> _diagnosticsExportRequests = Channel.CreateUnbounded<string>();
    private readonly Channel<AccessibilityProfileAssignmentCommandDto> _accessibilityProfileCommands = Channel.CreateUnbounded<AccessibilityProfileAssignmentCommandDto>();
    private readonly Channel<TeacherTtsCommandDto> _teacherTtsCommands = Channel.CreateUnbounded<TeacherTtsCommandDto>();
    private readonly Channel<StudentTeacherChatMessageDto> _teacherChatMessages = Channel.CreateUnbounded<StudentTeacherChatMessageDto>();
    private readonly Channel<RemoteControlSessionCommandDto> _remoteControlSessionCommands = Channel.CreateUnbounded<RemoteControlSessionCommandDto>();
    private readonly Channel<RemoteControlInputCommandDto> _remoteControlInputCommands = Channel.CreateUnbounded<RemoteControlInputCommandDto>();
    private readonly object _policySync = new();
    private HubConnection? _connection;
    private ResolvedStudentBinding? _binding;
    private string? _registeredConnectionId;
    private string? _registeredClientId;
    private string? _registeredIdentitySignature;
    private DetectionPolicyDto? _latestDetectionPolicy;
    private int _forceUnpairRequested;
    private string? _forceUnpairReason;

    /// <summary>
    /// Indicates whether hub is connected.
    /// </summary>
    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    /// <summary>
    /// Ensures connection and registration with server.
    /// </summary>
    public async Task<StudentConnectResult> EnsureConnectedAsync(ResolvedStudentBinding binding, StudentRuntimeIdentity identity, CancellationToken cancellationToken)
    {
        if (_connection is null
            || _binding is null
            || !string.Equals(_binding.ServerBaseUrl, binding.ServerBaseUrl, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_binding.ClientId, binding.ClientId, StringComparison.Ordinal))
        {
            await ResetConnectionAsync(cancellationToken);
            CreateConnection(binding);
        }

        _binding = binding;

        if (_connection is null)
        {
            return StudentConnectResult.Disconnected;
        }

        if (_connection.State == HubConnectionState.Disconnected)
        {
            await StartWithRetryAsync(cancellationToken);
        }

        if (_connection.State != HubConnectionState.Connected)
        {
            return StudentConnectResult.Disconnected;
        }

        var identitySignature = GetIdentitySignature(identity);

        // SignalR creates a new connection id after reconnect; registration must be repeated.
        if (string.Equals(_registeredConnectionId, _connection.ConnectionId, StringComparison.Ordinal)
            && string.Equals(_registeredClientId, binding.ClientId, StringComparison.Ordinal)
            && string.Equals(_registeredIdentitySignature, identitySignature, StringComparison.Ordinal))
        {
            return HasForceUnpairRequest() ? StudentConnectResult.ForceUnpair : StudentConnectResult.Connected;
        }

        var registration = new StudentRegistrationDto(
            binding.ClientId,
            binding.Token,
            identity.HostName,
            identity.UserName,
            identity.OsDescription,
            identity.LocalIpAddress);

        var registerResult = await _connection.InvokeAsync<StudentRegisterResultDto>("Register", registration, cancellationToken);
        if (!registerResult.Accepted)
        {
            logger.LogWarning(
                "Student registration rejected for client {ClientId} on connection {ConnectionId}: {Message}",
                binding.ClientId,
                _connection.ConnectionId,
                registerResult.Message);
            if (!string.IsNullOrWhiteSpace(registerResult.Message)
                && registerResult.Message.Contains("invalid token", StringComparison.OrdinalIgnoreCase))
            {
                MarkForceUnpair("Pairing token is no longer valid.");
                return StudentConnectResult.ForceUnpair;
            }

            return HasForceUnpairRequest() ? StudentConnectResult.ForceUnpair : StudentConnectResult.Disconnected;
        }

        _registeredConnectionId = _connection.ConnectionId;
        _registeredClientId = binding.ClientId;
        _registeredIdentitySignature = identitySignature;

        try
        {
            var policy = await _connection.InvokeAsync<DetectionPolicyDto>("GetDetectionPolicy", cancellationToken);
            lock (_policySync)
            {
                _latestDetectionPolicy = policy;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to fetch detection policy after registration.");
        }

        logger.LogInformation("Student registration accepted for client {ClientId} on connection {ConnectionId}", binding.ClientId, _registeredConnectionId);
        return HasForceUnpairRequest() ? StudentConnectResult.ForceUnpair : StudentConnectResult.Connected;
    }

    /// <summary>
    /// Sends screen frame payload.
    /// </summary>
    public Task SendFrameAsync(ScreenFrameDto frame, CancellationToken cancellationToken) =>
        SendAsync("SendFrame", frame, cancellationToken);

    /// <summary>
    /// Sends heartbeat payload.
    /// </summary>
    public Task SendHeartbeatAsync(HeartbeatDto heartbeat, CancellationToken cancellationToken) =>
        SendAsync("Heartbeat", heartbeat, cancellationToken);

    /// <summary>
    /// Sends detector alert.
    /// </summary>
    public Task SendAlertAsync(AlertEventDto alert, CancellationToken cancellationToken) =>
        SendAsync("SendAlert", alert, cancellationToken);

    /// <summary>
    /// Sends student interaction signal (for example hand raise).
    /// </summary>
    public Task SendStudentSignalAsync(StudentSignalEventDto signal, CancellationToken cancellationToken) =>
        SendAsync("SendStudentSignal", signal, cancellationToken);

    /// <summary>
    /// Sends student chat message to teacher server.
    /// </summary>
    public Task SendChatMessageAsync(StudentTeacherChatMessageDto message, CancellationToken cancellationToken) =>
        SendAsync("SendChatMessage", message, cancellationToken);

    /// <summary>
    /// Sends file progress update.
    /// </summary>
    public Task SendFileProgressAsync(FileDeliveryProgressDto progress, CancellationToken cancellationToken) =>
        SendAsync("ReportFileProgress", progress, cancellationToken);

    /// <summary>
    /// Sends remote control session status update.
    /// </summary>
    public Task SendRemoteControlStatusAsync(RemoteControlSessionStatusDto status, CancellationToken cancellationToken) =>
        SendAsync("ReportRemoteControlStatus", status, cancellationToken);

    /// <summary>
    /// Attempts to dequeue pending file transfer command.
    /// </summary>
    public bool TryDequeueTransferCommand(out FileTransferCommandDto command) =>
        _transferCommands.Reader.TryRead(out command!);

    /// <summary>
    /// Attempts to dequeue pending diagnostics export request id.
    /// </summary>
    public bool TryDequeueDiagnosticsExportRequest(out string requestId) =>
        _diagnosticsExportRequests.Reader.TryRead(out requestId!);

    /// <summary>
    /// Attempts to dequeue pending accessibility profile assignment command.
    /// </summary>
    public bool TryDequeueAccessibilityProfileCommand(out AccessibilityProfileAssignmentCommandDto command) =>
        _accessibilityProfileCommands.Reader.TryRead(out command!);

    /// <summary>
    /// Attempts to dequeue teacher TTS announcement command.
    /// </summary>
    public bool TryDequeueTeacherTtsCommand(out TeacherTtsCommandDto command) =>
        _teacherTtsCommands.Reader.TryRead(out command!);

    /// <summary>
    /// Attempts to dequeue teacher chat message command for endpoint overlay.
    /// </summary>
    public bool TryDequeueTeacherChatMessage(out StudentTeacherChatMessageDto message) =>
        _teacherChatMessages.Reader.TryRead(out message!);

    /// <summary>
    /// Attempts to dequeue pending remote-control session command.
    /// </summary>
    public bool TryDequeueRemoteControlSessionCommand(out RemoteControlSessionCommandDto command) =>
        _remoteControlSessionCommands.Reader.TryRead(out command!);

    /// <summary>
    /// Attempts to dequeue pending remote-control input command.
    /// </summary>
    public bool TryDequeueRemoteControlInputCommand(out RemoteControlInputCommandDto command) =>
        _remoteControlInputCommands.Reader.TryRead(out command!);

    /// <summary>
    /// Attempts to return latest detection policy from server.
    /// </summary>
    public bool TryGetLatestDetectionPolicy(out DetectionPolicyDto? policy)
    {
        lock (_policySync)
        {
            policy = _latestDetectionPolicy;
            return policy is not null;
        }
    }

    /// <summary>
    /// Disconnects active hub session.
    /// </summary>
    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        ResetConnectionAsync(cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await ResetConnectionAsync(CancellationToken.None);

    private async Task SendAsync(string method, object payload, CancellationToken cancellationToken)
    {
        if (_connection is null || _connection.State != HubConnectionState.Connected)
        {
            return;
        }

        await _connection.InvokeAsync(method, payload, cancellationToken);
    }

    private void CreateConnection(ResolvedStudentBinding binding)
    {
        var endpoint = $"{binding.ServerBaseUrl.TrimEnd('/')}{HubRoutes.StudentHub}";
        _connection = new HubConnectionBuilder()
            .WithUrl(endpoint, httpOptions =>
            {
                httpOptions.ApplicationMaxBufferSize = 4 * 1024 * 1024;
                httpOptions.TransportMaxBufferSize = 4 * 1024 * 1024;
            })
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            })
            .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)])
            .Build();

        _connection.On<FileTransferCommandDto>(HubMethods.FileTransferAssigned, command =>
        {
            _transferCommands.Writer.TryWrite(command);
        });

        _connection.On<string>(HubMethods.ForceUnpair, reason =>
        {
            MarkForceUnpair(string.IsNullOrWhiteSpace(reason) ? "Pairing was revoked by teacher." : reason);
        });

        _connection.On<DetectionPolicyDto>(HubMethods.DetectionPolicyUpdated, policy =>
        {
            lock (_policySync)
            {
                _latestDetectionPolicy = policy;
            }
        });

        _connection.On<string>(HubMethods.DetectionExportRequested, requestId =>
        {
            var normalized = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
            _diagnosticsExportRequests.Writer.TryWrite(normalized);
        });

        _connection.On<AccessibilityProfileAssignmentCommandDto>(HubMethods.AccessibilityProfileAssigned, command =>
        {
            _accessibilityProfileCommands.Writer.TryWrite(command);
        });

        _connection.On<TeacherTtsCommandDto>(HubMethods.TeacherTtsRequested, command =>
        {
            _teacherTtsCommands.Writer.TryWrite(command);
        });

        _connection.On<StudentTeacherChatMessageDto>(HubMethods.TeacherChatMessageRequested, message =>
        {
            _teacherChatMessages.Writer.TryWrite(message);
        });

        _connection.On<RemoteControlSessionCommandDto>(HubMethods.RemoteControlSessionCommand, command =>
        {
            _remoteControlSessionCommands.Writer.TryWrite(command);
        });

        _connection.On<RemoteControlInputCommandDto>(HubMethods.RemoteControlInputCommand, command =>
        {
            _remoteControlInputCommands.Writer.TryWrite(command);
        });

        _connection.Closed += async error =>
        {
            _registeredConnectionId = null;
            _registeredClientId = null;
            _registeredIdentitySignature = null;

            if (error is not null)
            {
                logger.LogWarning(error, "Student hub closed unexpectedly");
            }

            await Task.CompletedTask;
        };

        _connection.Reconnected += connectionId =>
        {
            _registeredConnectionId = null;
            _registeredIdentitySignature = null;
            logger.LogInformation("Student hub reconnected with id {ConnectionId}", connectionId);
            return Task.CompletedTask;
        };
    }

    private async Task StartWithRetryAsync(CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            return;
        }

        var attempt = 0;
        while (_connection.State == HubConnectionState.Disconnected && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                attempt++;
                await _connection.StartAsync(cancellationToken);
                logger.LogInformation("Connected to student hub");
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(6, attempt))));
                logger.LogWarning(ex, "Failed to connect student hub (attempt {Attempt}), retry in {Delay}", attempt, delay);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private async Task ResetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            return;
        }

        try
        {
            await _connection.StopAsync(cancellationToken);
        }
        catch
        {
            // ignored
        }

        try
        {
            await _connection.DisposeAsync();
        }
        finally
        {
            _connection = null;
            _registeredConnectionId = null;
            _registeredClientId = null;
            _registeredIdentitySignature = null;
            lock (_policySync)
            {
                _latestDetectionPolicy = null;
            }
        }
    }

    private static string GetIdentitySignature(StudentRuntimeIdentity identity) =>
        $"{identity.HostName}|{identity.UserName}|{identity.OsDescription}|{identity.LocalIpAddress}";

    private void MarkForceUnpair(string reason)
    {
        _forceUnpairReason = reason;
        Interlocked.Exchange(ref _forceUnpairRequested, 1);
        logger.LogWarning("Received force-unpair command: {Reason}", reason);
    }

    private bool HasForceUnpairRequest() =>
        Interlocked.CompareExchange(ref _forceUnpairRequested, 0, 0) == 1;

    /// <summary>
    /// Returns latest force-unpair reason and clears it.
    /// </summary>
    public string? ConsumeForceUnpairReason()
    {
        if (Interlocked.Exchange(ref _forceUnpairRequested, 0) == 0)
        {
            return null;
        }

        var reason = _forceUnpairReason;
        _forceUnpairReason = null;
        return reason ?? "Pairing was revoked.";
    }
}

/// <summary>
/// Result of student connection and registration attempt.
/// </summary>
public enum StudentConnectResult
{
    /// <summary>
    /// Hub is connected and student is registered.
    /// </summary>
    Connected = 1,

    /// <summary>
    /// Hub is disconnected or registration failed.
    /// </summary>
    Disconnected = 2,

    /// <summary>
    /// Pairing was revoked and local unpair is required.
    /// </summary>
    ForceUnpair = 3,
}

