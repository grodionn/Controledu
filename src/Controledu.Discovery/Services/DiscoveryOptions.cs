namespace Controledu.Discovery.Services;

/// <summary>
/// UDP discovery configuration options.
/// </summary>
public sealed class DiscoveryOptions
{
    /// <summary>
    /// Discovery UDP port.
    /// </summary>
    public int DiscoveryPort { get; set; } = 40555;

    /// <summary>
    /// Probe timeout in milliseconds for client scan.
    /// </summary>
    public int ProbeTimeoutMs { get; set; } = 1500;

    /// <summary>
    /// Number of broadcast probe bursts sent during a single discovery scan.
    /// </summary>
    public int ProbeRepeatCount { get; set; } = 2;

    /// <summary>
    /// Delay in milliseconds between probe bursts.
    /// </summary>
    public int ProbeRepeatIntervalMs { get; set; } = 120;
}
