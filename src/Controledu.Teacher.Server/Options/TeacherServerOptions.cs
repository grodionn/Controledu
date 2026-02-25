using Controledu.Transport.Constants;

namespace Controledu.Teacher.Server.Options;

/// <summary>
/// Runtime configuration for teacher server host.
/// </summary>
public sealed class TeacherServerOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "TeacherServer";

    /// <summary>
    /// Friendly server name shown to students.
    /// </summary>
    public string ServerName { get; set; } = Environment.MachineName;

    /// <summary>
    /// Optional persisted server id.
    /// </summary>
    public string? ServerId { get; set; }

    /// <summary>
    /// HTTP listening port.
    /// </summary>
    public int HttpPort { get; set; } = NetworkDefaults.TeacherHttpPort;

    /// <summary>
    /// UDP discovery port.
    /// </summary>
    public int DiscoveryPort { get; set; } = NetworkDefaults.DiscoveryUdpPort;

    /// <summary>
    /// Pairing code lifetime in seconds.
    /// </summary>
    public int PairingPinLifetimeSeconds { get; set; } = 60;

    /// <summary>
    /// Pairing token lifetime in hours.
    /// </summary>
    public int PairingTokenLifetimeHours { get; set; } = 24 * 30;

    /// <summary>
    /// SQLite storage file path. Relative path is rooted under common app data.
    /// </summary>
    public string StorageFile { get; set; } = "teacher-server.db";

    /// <summary>
    /// Transfer temp directory. Relative path is rooted under common app data.
    /// </summary>
    public string TransferRoot { get; set; } = "transfers";

    /// <summary>
    /// Enables HTTP endpoint without TLS for LAN MVP.
    /// </summary>
    public bool AllowHttp { get; set; } = true;

    /// <summary>
    /// Maximum inbound SignalR message size in bytes.
    /// </summary>
    public long SignalRMaxReceiveMessageBytes { get; set; } = 4 * 1024 * 1024;
}

