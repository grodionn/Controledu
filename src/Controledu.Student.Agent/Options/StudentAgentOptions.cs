using Controledu.Common.IO;
using Controledu.Transport.Constants;
using Controledu.Transport.Dto;

namespace Controledu.Student.Agent.Options;

/// <summary>
/// Runtime options for student background agent.
/// </summary>
public sealed class StudentAgentOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "StudentAgent";

    /// <summary>
    /// SQLite file path for student-side shared state.
    /// </summary>
    public string StorageFile { get; set; } = "student-shared.db";

    /// <summary>
    /// Loopback port of local Student.Host API/UI.
    /// </summary>
    public int LocalHostPort { get; set; } = NetworkDefaults.StudentLocalHostPort;

    /// <summary>
    /// Heartbeat interval in seconds.
    /// </summary>
    public int HeartbeatIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Baseline capture FPS.
    /// </summary>
    public int InitialFps { get; set; } = 45;

    /// <summary>
    /// Minimum capture FPS under poor connectivity.
    /// </summary>
    public int MinFps { get; set; } = 30;

    /// <summary>
    /// Maximum capture FPS.
    /// </summary>
    public int MaxFps { get; set; } = 60;

    /// <summary>
    /// Initial JPEG quality.
    /// </summary>
    public int InitialJpegQuality { get; set; } = 72;

    /// <summary>
    /// Minimum JPEG quality.
    /// </summary>
    public int MinJpegQuality { get; set; } = 28;

    /// <summary>
    /// Maximum JPEG quality.
    /// </summary>
    public int MaxJpegQuality { get; set; } = 88;

    /// <summary>
    /// Maximum encoded frame width for streaming.
    /// </summary>
    public int MaxCaptureWidth { get; set; } = 1920;

    /// <summary>
    /// Maximum encoded frame height for streaming.
    /// </summary>
    public int MaxCaptureHeight { get; set; } = 1080;

    /// <summary>
    /// Minimum interval between repeated hand-raise signals.
    /// </summary>
    public int HandRaiseCooldownSeconds { get; set; } = 20;

    /// <summary>
    /// Chunk size for file transfer.
    /// </summary>
    public int ChunkSize { get; set; } = ChunkingUtility.DefaultChunkSize;

    /// <summary>
    /// Local default detection policy (can be overridden by server policy).
    /// </summary>
    public DetectionPolicyDto Detection { get; set; } = new();

    /// <summary>
    /// Legacy keyword list used by old detector module.
    /// </summary>
    public string[] DetectorKeywords { get; set; } = ["chatgpt", "openai", "claude", "gemini"];

    /// <summary>
    /// Cloud TTS settings for teacher announcements.
    /// </summary>
    public StudentTeacherTtsOptions TeacherTts { get; set; } = new();
}

/// <summary>
/// TTS provider/runtime settings for teacher text announcements.
/// </summary>
public sealed class StudentTeacherTtsOptions
{
    /// <summary>
    /// Enables TTS announcement handling in the student agent.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Provider id: "google" or "disabled".
    /// </summary>
    public string Provider { get; set; } = "google";

    /// <summary>
    /// Google Cloud API key used for text:synthesize REST calls.
    /// </summary>
    public string? GoogleApiKey { get; set; }

    /// <summary>
    /// Default BCP-47 language code.
    /// </summary>
    public string LanguageCode { get; set; } = "ru-RU";

    /// <summary>
    /// Optional Google voice name (for example ru-RU-Wavenet-A).
    /// </summary>
    public string? VoiceName { get; set; }

    /// <summary>
    /// Speaking rate passed to provider request.
    /// </summary>
    public double SpeakingRate { get; set; } = 1.0;

    /// <summary>
    /// Pitch passed to provider request.
    /// </summary>
    public double Pitch { get; set; } = 0.0;

    /// <summary>
    /// Maximum accepted teacher message length.
    /// </summary>
    public int MaxTextLength { get; set; } = 600;

    /// <summary>
    /// If true, agent checks local accessibility profile flag before playing teacher TTS.
    /// </summary>
    public bool RespectAccessibilityToggle { get; set; } = true;
}
