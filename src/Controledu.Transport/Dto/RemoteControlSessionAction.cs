namespace Controledu.Transport.Dto;

/// <summary>
/// Session-level command sent from server to student agent.
/// </summary>
public enum RemoteControlSessionAction
{
    RequestStart = 1,
    Stop = 2,
}
