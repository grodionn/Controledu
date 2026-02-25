namespace Controledu.Teacher.Server.Models;

/// <summary>
/// Stable server identity payload.
/// </summary>
public sealed record ServerIdentity(
    string ServerId,
    string ServerName,
    string Fingerprint);
