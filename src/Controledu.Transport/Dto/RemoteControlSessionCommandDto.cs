namespace Controledu.Transport.Dto;

/// <summary>
/// Server-to-student remote control session command.
/// </summary>
public sealed record RemoteControlSessionCommandDto(
    string ClientId,
    string SessionId,
    RemoteControlSessionAction Action,
    DateTimeOffset RequestedAtUtc,
    string RequestedBy,
    int ApprovalTimeoutSeconds = 20,
    int MaxSessionSeconds = 600);
