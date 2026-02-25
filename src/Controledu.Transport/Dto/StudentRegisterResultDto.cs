namespace Controledu.Transport.Dto;

/// <summary>
/// Registration response for student SignalR clients.
/// </summary>
public sealed record StudentRegisterResultDto(
    bool Accepted,
    string Message,
    string? ServerName);

