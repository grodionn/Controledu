namespace Controledu.Common.Updates;

/// <summary>
/// Auto-update client settings for desktop hosts.
/// </summary>
public sealed class AutoUpdateOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "AutoUpdate";

    /// <summary>
    /// Enables background update checks and automatic installation.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Absolute URL to the update manifest JSON.
    /// </summary>
    public string ManifestUrl { get; set; } = string.Empty;

    /// <summary>
    /// Delay after UI startup before the first update check.
    /// </summary>
    public int StartupDelaySeconds { get; set; } = 8;

    /// <summary>
    /// Periodic background check interval.
    /// </summary>
    public int CheckIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Timeout for manifest and installer downloads.
    /// </summary>
    public int DownloadTimeoutSeconds { get; set; } = 600;
}
