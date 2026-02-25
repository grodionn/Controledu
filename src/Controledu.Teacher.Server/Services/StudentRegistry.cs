using Controledu.Transport.Dto;
using Controledu.Teacher.Server.Models;
using System.Collections.Concurrent;

namespace Controledu.Teacher.Server.Services;

/// <summary>
/// In-memory student presence registry.
/// </summary>
public interface IStudentRegistry
{
    /// <summary>
    /// Registers or refreshes a student session.
    /// </summary>
    StudentInfoDto Upsert(string connectionId, StudentRegistrationDto registration, DateTimeOffset nowUtc);

    /// <summary>
    /// Marks session disconnected by connection id.
    /// </summary>
    StudentInfoDto? Disconnect(string connectionId, DateTimeOffset nowUtc);

    /// <summary>
    /// Updates heartbeat timestamp.
    /// </summary>
    StudentInfoDto? Heartbeat(string clientId, DateTimeOffset nowUtc);

    /// <summary>
    /// Lists all students.
    /// </summary>
    IReadOnlyList<StudentInfoDto> GetAll();

    /// <summary>
    /// Resolves active SignalR connection id.
    /// </summary>
    bool TryGetConnectionId(string clientId, out string? connectionId);

    /// <summary>
    /// Removes student from in-memory registry.
    /// </summary>
    bool Remove(string clientId);

    /// <summary>
    /// Applies detection status flag to all known students.
    /// </summary>
    void SetDetectionEnabledForAll(bool enabled);

    /// <summary>
    /// Updates detection state from an alert event.
    /// </summary>
    StudentInfoDto? UpdateDetectionAlert(AlertEventDto alert);
}

internal sealed class StudentRegistry : IStudentRegistry
{
    private readonly ConcurrentDictionary<string, ConnectedStudentSession> _students = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _connections = new(StringComparer.Ordinal);

    public StudentInfoDto Upsert(string connectionId, StudentRegistrationDto registration, DateTimeOffset nowUtc)
    {
        var session = _students.AddOrUpdate(
            registration.ClientId,
            _ => new ConnectedStudentSession
            {
                ClientId = registration.ClientId,
                ConnectionId = connectionId,
                HostName = registration.HostName,
                UserName = registration.UserName,
                LocalIpAddress = registration.LocalIpAddress,
                LastSeenUtc = nowUtc,
                IsOnline = true,
                DetectionEnabled = true,
            },
            (_, current) =>
            {
                current.ConnectionId = connectionId;
                current.HostName = registration.HostName;
                current.UserName = registration.UserName;
                current.LocalIpAddress = registration.LocalIpAddress;
                current.LastSeenUtc = nowUtc;
                current.IsOnline = true;
                return current;
            });

        _connections[connectionId] = registration.ClientId;
        return session.ToDto();
    }

    public StudentInfoDto? Disconnect(string connectionId, DateTimeOffset nowUtc)
    {
        if (!_connections.TryRemove(connectionId, out var clientId))
        {
            return null;
        }

        if (!_students.TryGetValue(clientId, out var session))
        {
            return null;
        }

        session.IsOnline = false;
        session.LastSeenUtc = nowUtc;
        return session.ToDto();
    }

    public StudentInfoDto? Heartbeat(string clientId, DateTimeOffset nowUtc)
    {
        if (!_students.TryGetValue(clientId, out var session))
        {
            return null;
        }

        session.LastSeenUtc = nowUtc;
        return session.ToDto();
    }

    public IReadOnlyList<StudentInfoDto> GetAll() =>
        _students.Values
            .Select(static x => x.ToDto())
            .OrderBy(x => x.HostName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public bool TryGetConnectionId(string clientId, out string? connectionId)
    {
        connectionId = _students.TryGetValue(clientId, out var session) && session.IsOnline
            ? session.ConnectionId
            : null;

        return !string.IsNullOrWhiteSpace(connectionId);
    }

    public bool Remove(string clientId)
    {
        if (!_students.TryRemove(clientId, out var session))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(session.ConnectionId))
        {
            _connections.TryRemove(session.ConnectionId, out _);
        }

        return true;
    }

    public void SetDetectionEnabledForAll(bool enabled)
    {
        foreach (var session in _students.Values)
        {
            session.DetectionEnabled = enabled;
        }
    }

    public StudentInfoDto? UpdateDetectionAlert(AlertEventDto alert)
    {
        if (!_students.TryGetValue(alert.StudentId, out var session))
        {
            return null;
        }

        session.LastDetectionAtUtc = alert.TimestampUtc;
        session.LastDetectionClass = alert.DetectionClass.ToString();
        session.LastDetectionConfidence = alert.Confidence;
        session.AlertCount++;
        return session.ToDto();
    }
}

