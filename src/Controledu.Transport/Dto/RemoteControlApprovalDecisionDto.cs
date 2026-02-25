namespace Controledu.Transport.Dto;

/// <summary>
/// Student-side approval decision for a pending remote control request.
/// Shared via local host/agent runtime storage.
/// </summary>
public sealed record RemoteControlApprovalDecisionDto(
    string SessionId,
    bool Approved,
    DateTimeOffset DecidedAtUtc,
    string? Message = null);
