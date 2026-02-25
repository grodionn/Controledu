using Controledu.Detection.Abstractions;

namespace Controledu.Detection.Core;

/// <summary>
/// Metadata-rule detector based on active process/title/url patterns.
/// </summary>
public sealed class WindowMetadataDetector : IAiUiDetector
{
    private static readonly Dictionary<string, DetectionClass> ClassMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chatgpt"] = DetectionClass.ChatGpt,
        ["openai"] = DetectionClass.ChatGpt,
        ["claude"] = DetectionClass.Claude,
        ["anthropic"] = DetectionClass.Claude,
        ["gemini"] = DetectionClass.Gemini,
        ["bard"] = DetectionClass.Gemini,
        ["copilot"] = DetectionClass.Copilot,
        ["perplexity"] = DetectionClass.Perplexity,
        ["deepseek"] = DetectionClass.DeepSeek,
        ["poe"] = DetectionClass.Poe,
        ["grok"] = DetectionClass.Grok,
        ["qwen"] = DetectionClass.Qwen,
        ["mistral"] = DetectionClass.Mistral,
        ["meta ai"] = DetectionClass.MetaAi,
        ["meta.ai"] = DetectionClass.MetaAi,
    };

    /// <inheritdoc />
    public string Name => nameof(WindowMetadataDetector);

    /// <inheritdoc />
    public Task<DetectionResult?> AnalyzeAsync(DetectionObservation observation, DetectionSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(settings);

        var title = observation.ActiveWindowTitle;
        var process = observation.ActiveProcessName;
        var url = observation.BrowserHintUrl;

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(process) && string.IsNullOrWhiteSpace(url))
        {
            return Task.FromResult<DetectionResult?>(null);
        }

        var combined = string.Join(
            " ",
            new[] { title, process, url }
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Select(static x => x!.ToLowerInvariant()));

        foreach (var safePattern in settings.WhitelistKeywords)
        {
            if (!string.IsNullOrWhiteSpace(safePattern) && combined.Contains(safePattern.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<DetectionResult?>(
                    new DetectionResult(
                        false,
                        0,
                        DetectionClass.None,
                        DetectionStageSource.MetadataRule,
                        $"Whitelist match: {safePattern}",
                        null,
                        [safePattern],
                        false));
            }
        }

        var matched = new List<string>();
        var detectedClass = DetectionClass.None;

        foreach (var keyword in settings.Keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                continue;
            }

            if (!combined.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            matched.Add(keyword);
            if (detectedClass == DetectionClass.None && ClassMap.TryGetValue(keyword.Trim(), out var mappedClass))
            {
                detectedClass = mappedClass;
            }
        }

        if (matched.Count == 0)
        {
            return Task.FromResult<DetectionResult?>(null);
        }

        if (detectedClass == DetectionClass.None)
        {
            detectedClass = DetectionClass.UnknownAi;
        }

        var confidence = 0.62 + Math.Min(0.30, matched.Count * 0.08);
        if (!string.IsNullOrWhiteSpace(url))
        {
            confidence = Math.Min(0.98, confidence + 0.08);
        }

        return Task.FromResult<DetectionResult?>(
            new DetectionResult(
                true,
                confidence,
                detectedClass,
                DetectionStageSource.MetadataRule,
                "Metadata keyword rule matched",
                null,
                matched,
                false));
    }
}
