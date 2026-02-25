namespace Controledu.Transport.Dto;

/// <summary>
/// Runtime detection policy distributed to student agents.
/// </summary>
public sealed record DetectionPolicyDto
{
    /// <summary>
    /// Enables or disables detection pipeline.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Evaluation interval in seconds.
    /// </summary>
    public int EvaluationIntervalSeconds { get; init; } = 5;

    /// <summary>
    /// Frame hash distance threshold.
    /// </summary>
    public int FrameChangeThreshold { get; init; } = 6;

    /// <summary>
    /// Minimum forced recheck interval for unchanged frames.
    /// </summary>
    public int MinRecheckIntervalSeconds { get; init; } = 15;

    /// <summary>
    /// Metadata stage confidence threshold.
    /// </summary>
    public double MetadataThreshold { get; init; } = 0.55;

    /// <summary>
    /// ML stage confidence threshold.
    /// </summary>
    public double MlThreshold { get; init; } = 0.70;

    /// <summary>
    /// Temporal window size.
    /// </summary>
    public int TemporalWindowSize { get; init; } = 3;

    /// <summary>
    /// Required positives in temporal window.
    /// </summary>
    public int TemporalRequiredVotes { get; init; } = 2;

    /// <summary>
    /// Cooldown for duplicate alerts.
    /// </summary>
    public int CooldownSeconds { get; init; } = 10;

    /// <summary>
    /// Keyword list for metadata detector.
    /// </summary>
    public string[] Keywords { get; init; } =
    [
        "chatgpt",
        "openai",
        "claude",
        "anthropic",
        "gemini",
        "bard",
        "copilot",
        "perplexity",
        "deepseek",
        "poe"
    ];

    /// <summary>
    /// Whitelist patterns.
    /// </summary>
    public string[] WhitelistKeywords { get; init; } = [];

    /// <summary>
    /// Enables local dataset collection mode.
    /// </summary>
    public bool DataCollectionModeEnabled { get; init; }

    /// <summary>
    /// Min interval between stored samples.
    /// </summary>
    public int DataCollectionMinIntervalSeconds { get; init; } = 15;

    /// <summary>
    /// Probability for storing a changed sample (0..1).
    /// </summary>
    public double DataCollectionSampleRate { get; init; } = 0.35;

    /// <summary>
    /// Retention in days for local dataset snapshots.
    /// </summary>
    public int DataCollectionRetentionDays { get; init; } = 7;

    /// <summary>
    /// Whether to store full frames in dataset mode.
    /// </summary>
    public bool DataCollectionStoreFullFrames { get; init; } = true;

    /// <summary>
    /// Whether to store thumbnails in dataset mode.
    /// </summary>
    public bool DataCollectionStoreThumbnails { get; init; } = true;

    /// <summary>
    /// Whether to include tiny thumbnails inside alert events.
    /// </summary>
    public bool IncludeAlertThumbnails { get; init; } = true;

    /// <summary>
    /// Alert thumbnail width in pixels.
    /// </summary>
    public int AlertThumbnailWidth { get; init; } = 280;

    /// <summary>
    /// Alert thumbnail height in pixels.
    /// </summary>
    public int AlertThumbnailHeight { get; init; } = 160;

    /// <summary>
    /// Optional policy version tag.
    /// </summary>
    public string PolicyVersion { get; init; } = "mvp-1";
}
