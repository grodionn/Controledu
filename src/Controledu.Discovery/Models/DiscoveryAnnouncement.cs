namespace Controledu.Discovery.Models;

/// <summary>
/// Discovery announcement payload for responders.
/// </summary>
public sealed record DiscoveryAnnouncement(
    string ServerId,
    string ServerName,
    string Host,
    int Port);
