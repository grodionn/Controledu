using Controledu.Transport.Constants;
using System.ComponentModel.DataAnnotations;

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
    [Range(1, 65535)]
    public int HttpPort { get; set; } = NetworkDefaults.TeacherHttpPort;

    /// <summary>
    /// UDP discovery port.
    /// </summary>
    [Range(1, 65535)]
    public int DiscoveryPort { get; set; } = NetworkDefaults.DiscoveryUdpPort;

    /// <summary>
    /// Pairing code lifetime in seconds.
    /// </summary>
    [Range(10, 3600)]
    public int PairingPinLifetimeSeconds { get; set; } = 60;

    /// <summary>
    /// Pairing token lifetime in hours.
    /// </summary>
    [Range(1, 24 * 365)]
    public int PairingTokenLifetimeHours { get; set; } = 24 * 30;

    /// <summary>
    /// SQLite storage file path. Relative path is rooted under common app data.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string StorageFile { get; set; } = "teacher-server.db";

    /// <summary>
    /// Transfer temp directory. Relative path is rooted under common app data.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string TransferRoot { get; set; } = "transfers";

    /// <summary>
    /// Maximum inbound SignalR message size in bytes.
    /// </summary>
    [Range(32 * 1024, 64L * 1024L * 1024L)]
    public long SignalRMaxReceiveMessageBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>
    /// Optional pre-shared teacher API token. If empty, server generates and persists one.
    /// </summary>
    public string? TeacherApiToken { get; set; }

    /// <summary>
    /// Optional CORS origin allowlist for teacher UI in browser mode.
    /// Loopback origins are always allowed.
    /// </summary>
    public string[] AllowedCorsOrigins { get; set; } =
    [
        "http://localhost:5173",
        "http://127.0.0.1:5173",
    ];
}
