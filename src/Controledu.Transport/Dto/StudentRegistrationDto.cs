namespace Controledu.Transport.Dto;

/// <summary>
/// Student registration payload for SignalR session.
/// </summary>
public sealed record StudentRegistrationDto(
    string ClientId,
    string Token,
    string HostName,
    string UserName,
    string OsDescription,
    string? LocalIpAddress);

