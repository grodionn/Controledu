namespace Controledu.Transport.Dto;

/// <summary>
/// Student-to-teacher remote control session status event.
/// </summary>
public sealed record RemoteControlSessionStatusDto(
    string StudentId,
    string StudentDisplayName,
    string SessionId,
    RemoteControlSessionState State,
    DateTimeOffset TimestampUtc,
    string? Message = null);
