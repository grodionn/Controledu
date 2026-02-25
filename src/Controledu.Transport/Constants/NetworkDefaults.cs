namespace Controledu.Transport.Constants;

/// <summary>
/// Default network ports and protocol constants.
/// </summary>
public static class NetworkDefaults
{
    /// <summary>
    /// UDP discovery port.
    /// </summary>
    public const int DiscoveryUdpPort = 40555;

    /// <summary>
    /// Teacher server HTTP port.
    /// </summary>
    public const int TeacherHttpPort = 40556;

    /// <summary>
    /// Student local UI/API host port.
    /// </summary>
    public const int StudentLocalHostPort = 40557;

    /// <summary>
    /// Discovery request payload.
    /// </summary>
    public const string DiscoveryProbe = "DISCOVER_CONTROLEDU";

    /// <summary>
    /// IPv4 multicast group used as a discovery fallback when broadcast is filtered between Wi-Fi and LAN segments.
    /// </summary>
    public const string DiscoveryMulticastGroup = "239.255.77.55";
}

