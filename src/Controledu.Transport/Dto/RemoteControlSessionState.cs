namespace Controledu.Transport.Dto;

/// <summary>
/// Runtime remote control session state reported by student agent.
/// </summary>
public enum RemoteControlSessionState
{
    PendingApproval = 1,
    Approved = 2,
    Rejected = 3,
    Ended = 4,
    Expired = 5,
    Error = 6,
}
