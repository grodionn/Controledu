namespace Controledu.Transport.Dto;

/// <summary>
/// Provides hardened production detection policy defaults.
/// </summary>
public static class DetectionPolicyFactory
{
    private static readonly string[] DefaultKeywords =
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
        "poe",
        "grok",
        "qwen",
        "mistral",
        "meta ai",
        "meta.ai",
    ];

    private static readonly string[] DefaultWhitelist =
    [
        "wikipedia.org",
        "docs.python.org",
        "learn.microsoft.com"
    ];

    /// <summary>
    /// Creates fixed production policy.
    /// </summary>
    public static DetectionPolicyDto CreateProductionPolicy(bool enabled = true) =>
        new()
        {
            Enabled = enabled,
            EvaluationIntervalSeconds = 5,
            FrameChangeThreshold = 6,
            MinRecheckIntervalSeconds = 5,
            MetadataThreshold = 0.64,
            MlThreshold = 0.72,
            TemporalWindowSize = 3,
            TemporalRequiredVotes = 2,
            CooldownSeconds = 10,
            Keywords = DefaultKeywords.ToArray(),
            WhitelistKeywords = DefaultWhitelist.ToArray(),
            DataCollectionModeEnabled = false,
            DataCollectionMinIntervalSeconds = 30,
            DataCollectionSampleRate = 0,
            DataCollectionRetentionDays = 1,
            DataCollectionStoreFullFrames = false,
            DataCollectionStoreThumbnails = false,
            IncludeAlertThumbnails = true,
            AlertThumbnailWidth = 280,
            AlertThumbnailHeight = 160,
            PolicyVersion = "production-hardcoded-v1",
        };
}
