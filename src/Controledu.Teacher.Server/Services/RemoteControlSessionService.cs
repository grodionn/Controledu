using Controledu.Transport.Dto;
using System.Collections.Concurrent;

namespace Controledu.Teacher.Server.Services;

/// <summary>
/// Tracks in-memory remote control sessions per student and teacher connection.
/// </summary>
public interface IRemoteControlSessionService
{
    RemoteControlSessionLease Start(string clientId, string teacherConnectionId, DateTimeOffset nowUtc);
    bool TryGet(string clientId, out RemoteControlSessionLease? lease);
    bool CanForwardInput(RemoteControlInputCommandDto command, string teacherConnectionId);
    bool TryApplyStudentStatus(RemoteControlSessionStatusDto status, out RemoteControlSessionLease? lease);
    bool TryStop(string clientId, string teacherConnectionId, DateTimeOffset nowUtc, out RemoteControlSessionLease? lease);
    IReadOnlyList<RemoteControlSessionLease> RemoveTeacherSessions(string teacherConnectionId, DateTimeOffset nowUtc);
}

public sealed record RemoteControlSessionLease(
    string ClientId,
    string SessionId,
    string TeacherConnectionId,
    RemoteControlSessionState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

internal sealed class RemoteControlSessionService : IRemoteControlSessionService
{
    private sealed class SessionEntry
    {
        public required string ClientId { get; init; }
        public required string TeacherConnectionId { get; init; }
        public required string SessionId { get; init; }
        public required DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public RemoteControlSessionState State { get; set; }
    }

    private readonly ConcurrentDictionary<string, SessionEntry> _sessionsByClientId = new(StringComparer.Ordinal);

    public RemoteControlSessionLease Start(string clientId, string teacherConnectionId, DateTimeOffset nowUtc)
    {
        var entry = new SessionEntry
        {
            ClientId = clientId,
            TeacherConnectionId = teacherConnectionId,
            SessionId = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            State = RemoteControlSessionState.PendingApproval,
        };

        _sessionsByClientId[clientId] = entry;
        return ToLease(entry);
    }

    public bool TryGet(string clientId, out RemoteControlSessionLease? lease)
    {
        if (_sessionsByClientId.TryGetValue(clientId, out var entry))
        {
            lease = ToLease(entry);
            return true;
        }

        lease = null;
        return false;
    }

    public bool CanForwardInput(RemoteControlInputCommandDto command, string teacherConnectionId)
    {
        if (!_sessionsByClientId.TryGetValue(command.ClientId, out var entry))
        {
            return false;
        }

        return string.Equals(entry.TeacherConnectionId, teacherConnectionId, StringComparison.Ordinal)
            && string.Equals(entry.SessionId, command.SessionId, StringComparison.Ordinal)
            && entry.State == RemoteControlSessionState.Approved;
    }

    public bool TryApplyStudentStatus(RemoteControlSessionStatusDto status, out RemoteControlSessionLease? lease)
    {
        if (!_sessionsByClientId.TryGetValue(status.StudentId, out var entry))
        {
            lease = null;
            return false;
        }

        if (!string.Equals(entry.SessionId, status.SessionId, StringComparison.Ordinal))
        {
            lease = null;
            return false;
        }

        entry.State = status.State;
        entry.UpdatedAtUtc = status.TimestampUtc;
        lease = ToLease(entry);

        if (status.State is RemoteControlSessionState.Ended or RemoteControlSessionState.Expired or RemoteControlSessionState.Rejected or RemoteControlSessionState.Error)
        {
            _sessionsByClientId.TryRemove(status.StudentId, out _);
        }

        return true;
    }

    public bool TryStop(string clientId, string teacherConnectionId, DateTimeOffset nowUtc, out RemoteControlSessionLease? lease)
    {
        if (!_sessionsByClientId.TryGetValue(clientId, out var entry))
        {
            lease = null;
            return false;
        }

        if (!string.Equals(entry.TeacherConnectionId, teacherConnectionId, StringComparison.Ordinal))
        {
            lease = null;
            return false;
        }

        entry.UpdatedAtUtc = nowUtc;
        entry.State = RemoteControlSessionState.Ended;
        lease = ToLease(entry);
        _sessionsByClientId.TryRemove(clientId, out _);
        return true;
    }

    public IReadOnlyList<RemoteControlSessionLease> RemoveTeacherSessions(string teacherConnectionId, DateTimeOffset nowUtc)
    {
        var removed = new List<RemoteControlSessionLease>();
        foreach (var pair in _sessionsByClientId.ToArray())
        {
            if (!string.Equals(pair.Value.TeacherConnectionId, teacherConnectionId, StringComparison.Ordinal))
            {
                continue;
            }

            if (_sessionsByClientId.TryRemove(pair.Key, out var entry))
            {
                entry.State = RemoteControlSessionState.Ended;
                entry.UpdatedAtUtc = nowUtc;
                removed.Add(ToLease(entry));
            }
        }

        return removed;
    }

    private static RemoteControlSessionLease ToLease(SessionEntry entry) =>
        new(
            entry.ClientId,
            entry.SessionId,
            entry.TeacherConnectionId,
            entry.State,
            entry.CreatedAtUtc,
            entry.UpdatedAtUtc);
}
