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

    private static readonly HashSet<string> BrowserProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome",
        "msedge",
        "firefox",
        "opera",
        "opera_gx",
        "brave",
        "vivaldi",
        "yandexbrowser",
        "chromium",
        "iexplore",
    };

    /// <inheritdoc />
    public string Name => nameof(WindowMetadataDetector);

    /// <inheritdoc />
    public Task<DetectionResult?> AnalyzeAsync(DetectionObservation observation, DetectionSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(settings);

        var title = observation.ActiveWindowTitle?.Trim();
        var process = observation.ActiveProcessName?.Trim();
        var url = observation.BrowserHintUrl?.Trim();

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(process) && string.IsNullOrWhiteSpace(url))
        {
            return Task.FromResult<DetectionResult?>(null);
        }

        var normalizedTitle = title?.ToLowerInvariant() ?? string.Empty;
        var normalizedProcess = process?.ToLowerInvariant() ?? string.Empty;
        var normalizedUrl = url?.ToLowerInvariant() ?? string.Empty;

        var whitelistMatch = FindWhitelistMatch(settings.WhitelistKeywords, normalizedTitle, normalizedProcess, normalizedUrl);
        if (whitelistMatch is not null)
        {
            return Task.FromResult<DetectionResult?>(
                new DetectionResult(
                    false,
                    0,
                    DetectionClass.None,
                    DetectionStageSource.MetadataRule,
                    $"Whitelist match: {whitelistMatch}",
                    null,
                    [whitelistMatch],
                    false));
        }

        var matchedKeywords = new List<string>();
        var detectedClass = DetectionClass.None;
        var titleHits = 0;
        var processHits = 0;
        var urlHits = 0;

        foreach (var keywordRaw in settings.Keywords)
        {
            var keyword = keywordRaw?.Trim();
            if (string.IsNullOrWhiteSpace(keyword))
            {
                continue;
            }

            var normalizedKeyword = keyword.ToLowerInvariant();
            var inTitle = ContainsPattern(normalizedTitle, normalizedKeyword);
            var inProcess = ContainsPattern(normalizedProcess, normalizedKeyword);
            var inUrl = ContainsPattern(normalizedUrl, normalizedKeyword);
            if (!inTitle && !inProcess && !inUrl)
            {
                continue;
            }

            matchedKeywords.Add(keyword);
            if (inTitle)
            {
                titleHits++;
            }

            if (inProcess)
            {
                processHits++;
            }

            if (inUrl)
            {
                urlHits++;
            }

            if (detectedClass == DetectionClass.None && ClassMap.TryGetValue(normalizedKeyword, out var mappedClass))
            {
                detectedClass = mappedClass;
            }
        }

        if (matchedKeywords.Count == 0)
        {
            return Task.FromResult<DetectionResult?>(null);
        }

        var isBrowser = BrowserProcessNames.Contains(normalizedProcess);
        var hasUrlEvidence = urlHits > 0;
        var hasStrongProcessEvidence = processHits > 0 && !isBrowser;
        var hasMultiKeywordEvidence = matchedKeywords.Count >= 2;
        var hasCrossFieldEvidence = titleHits > 0 && processHits > 0;

        if (!hasUrlEvidence && !hasStrongProcessEvidence && !hasMultiKeywordEvidence && !hasCrossFieldEvidence)
        {
            return Task.FromResult<DetectionResult?>(
                new DetectionResult(
                    false,
                    0,
                    DetectionClass.None,
                    DetectionStageSource.MetadataRule,
                    "Metadata keyword match ignored: insufficient evidence.",
                    null,
                    matchedKeywords,
                    false));
        }

        if (detectedClass == DetectionClass.None)
        {
            detectedClass = DetectionClass.UnknownAi;
        }

        var confidence = 0.58 + Math.Min(0.24, matchedKeywords.Count * 0.07);
        if (processHits > 0)
        {
            confidence += 0.06;
        }

        if (hasUrlEvidence)
        {
            confidence += 0.12;
        }

        confidence = Math.Clamp(confidence, 0, 0.98);

        return Task.FromResult<DetectionResult?>(
            new DetectionResult(
                true,
                confidence,
                detectedClass,
                DetectionStageSource.MetadataRule,
                "Metadata keyword rule matched",
                null,
                matchedKeywords,
                false));
    }

    private static string? FindWhitelistMatch(IEnumerable<string> whitelist, string title, string process, string url)
    {
        foreach (var raw in whitelist)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var pattern = raw.Trim().ToLowerInvariant();
            foreach (var candidate in ExpandWhitelistCandidates(pattern))
            {
                if (ContainsPattern(title, candidate)
                    || ContainsPattern(process, candidate)
                    || ContainsPattern(url, candidate))
                {
                    return raw.Trim();
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> ExpandWhitelistCandidates(string pattern)
    {
        yield return pattern;

        var withoutScheme = pattern;
        if (withoutScheme.Contains("://", StringComparison.Ordinal))
        {
            var separatorIndex = withoutScheme.IndexOf("://", StringComparison.Ordinal);
            withoutScheme = separatorIndex >= 0 ? withoutScheme[(separatorIndex + 3)..] : withoutScheme;
        }

        var slashIndex = withoutScheme.IndexOf('/');
        if (slashIndex >= 0)
        {
            withoutScheme = withoutScheme[..slashIndex];
        }

        if (string.IsNullOrWhiteSpace(withoutScheme))
        {
            yield break;
        }

        var host = withoutScheme.Trim();
        if (!string.Equals(host, pattern, StringComparison.Ordinal))
        {
            yield return host;
        }

        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            var withoutWww = host[4..];
            if (!string.IsNullOrWhiteSpace(withoutWww))
            {
                yield return withoutWww;
                host = withoutWww;
            }
        }

        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (labels.Length == 2 && labels[0].Length >= 8)
        {
            yield return labels[0];
        }
    }

    private static bool ContainsPattern(string source, string pattern)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        var startIndex = 0;
        while (startIndex < source.Length)
        {
            var index = source.IndexOf(pattern, startIndex, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            var beforeOk = index == 0 || !char.IsLetterOrDigit(source[index - 1]);
            var end = index + pattern.Length;
            var afterOk = end >= source.Length || !char.IsLetterOrDigit(source[end]);
            if (beforeOk && afterOk)
            {
                return true;
            }

            startIndex = index + 1;
        }

        return false;
    }
}
