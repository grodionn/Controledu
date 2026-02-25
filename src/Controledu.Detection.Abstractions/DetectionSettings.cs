namespace Controledu.Detection.Abstractions;

/// <summary>
/// Runtime detection settings consumed by the pipeline.
/// </summary>
public sealed class DetectionSettings
{
    /// <summary>
    /// Enables or disables detection.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Hash-distance threshold for frame-change filtering.
    /// </summary>
    public int FrameChangeThreshold { get; set; } = 6;

    /// <summary>
    /// Minimum re-check interval for unchanged frames.
    /// </summary>
    public int MinRecheckIntervalSeconds { get; set; } = 15;

    /// <summary>
    /// Minimum confidence for metadata-rule positives.
    /// </summary>
    public double MetadataConfidenceThreshold { get; set; } = 0.55;

    /// <summary>
    /// Minimum confidence for ML positives.
    /// </summary>
    public double MlConfidenceThreshold { get; set; } = 0.70;

    /// <summary>
    /// Temporal voting window size.
    /// </summary>
    public int TemporalWindowSize { get; set; } = 3;

    /// <summary>
    /// Required positive votes within temporal window.
    /// </summary>
    public int TemporalRequiredVotes { get; set; } = 2;

    /// <summary>
    /// Cooldown for duplicate alerts in seconds.
    /// </summary>
    public int CooldownSeconds { get; set; } = 20;

    /// <summary>
    /// Metadata keyword dictionary.
    /// </summary>
    public string[] Keywords { get; set; } =
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
    /// Metadata whitelist dictionary.
    /// </summary>
    public string[] WhitelistKeywords { get; set; } = [];
}
