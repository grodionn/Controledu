using Controledu.Transport.Constants;

namespace Controledu.Student.Host.Options;

/// <summary>
/// Runtime options for the student desktop host.
/// </summary>
public sealed class StudentHostOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "StudentHost";

    /// <summary>
    /// Loopback port for local web UI/API host.
    /// </summary>
    public int LocalPort { get; set; } = NetworkDefaults.StudentLocalHostPort;

    /// <summary>
    /// Shared SQLite storage file path.
    /// </summary>
    public string StorageFile { get; set; } = "student-shared.db";

    /// <summary>
    /// Discovery UDP port.
    /// </summary>
    public int DiscoveryPort { get; set; } = NetworkDefaults.DiscoveryUdpPort;

    /// <summary>
    /// Discovery timeout in milliseconds.
    /// </summary>
    public int DiscoveryTimeoutMs { get; set; } = 1500;

    /// <summary>
    /// Optional explicit path to Student.Agent executable.
    /// </summary>
    public string AgentExecutablePath { get; set; } = "StudentAgent/Controledu.Student.Agent.exe";

    /// <summary>
    /// Window title.
    /// </summary>
    public string WindowTitle { get; set; } = "Controledu Endpoint";

    /// <summary>
    /// Starts window maximized.
    /// </summary>
    public bool StartMaximized { get; set; }

    /// <summary>
    /// Cooldown between repeated hand-raise requests from overlay.
    /// </summary>
    public int HandRaiseCooldownSeconds { get; set; } = 20;
}
