namespace Controledu.Common.Models;

/// <summary>
/// Runtime identity snapshot for a student device.
/// </summary>
public sealed record StudentRuntimeIdentity(
    string HostName,
    string UserName,
    string OsDescription,
    string? LocalIpAddress);
