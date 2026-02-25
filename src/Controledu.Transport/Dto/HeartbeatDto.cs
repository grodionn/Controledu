namespace Controledu.Transport.Dto;

/// <summary>
/// Heartbeat payload from student.
/// </summary>
public sealed record HeartbeatDto(
    string ClientId,
    DateTimeOffset SentAtUtc);

