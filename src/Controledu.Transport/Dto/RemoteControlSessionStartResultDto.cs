namespace Controledu.Transport.Dto;

/// <summary>
/// Teacher request result for starting remote control session.
/// </summary>
public sealed record RemoteControlSessionStartResultDto(
    bool Accepted,
    string? SessionId,
    string Message);
