using Controledu.Detection.Abstractions;
using Microsoft.Extensions.Logging;

namespace Controledu.Detection.Core;

/// <summary>
/// Pipeline decision payload.
/// </summary>
public sealed record DetectionPipelineDecision(
    DetectionObservation Observation,
    DetectionResult Result,
    bool ShouldEmitAlert,
    bool UsedCachedResult);

/// <summary>
/// Multi-stage AI UI detection pipeline.
/// </summary>
public sealed class DetectionPipeline(
    IFrameChangeFilter frameChangeFilter,
    WindowMetadataDetector metadataDetector,
    IEnumerable<IAiUiDetector> mlDetectors,
    ITemporalSmoother temporalSmoother,
    ILogger<DetectionPipeline> logger)
{
    private DetectionResult? _lastRawResult;

    /// <summary>
    /// Runs stage A/B/C/D and returns fused decision.
    /// </summary>
    public async Task<DetectionPipelineDecision> AnalyzeAsync(
        DetectionObservation observation,
        DetectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled)
        {
            var disabled = DetectionResult.None("Detection disabled by policy.");
            return new DetectionPipelineDecision(observation, disabled, false, false);
        }

        var frameState = frameChangeFilter.Evaluate(observation, settings);
        var prepared = observation with
        {
            ScreenFrameHash = frameState.ScreenFrameHash,
            FrameChanged = frameState.FrameChanged,
        };

        if (!frameState.ShouldAnalyze && _lastRawResult is not null)
        {
            var reused = _lastRawResult with { Reason = "Frame unchanged; reused previous detection." };
            var smoothedReused = temporalSmoother.Apply(reused, settings, prepared.TimestampUtc);
            return new DetectionPipelineDecision(prepared, smoothedReused.Result, false, true);
        }

        var candidates = new List<DetectionResult>();

        var metadata = await metadataDetector.AnalyzeAsync(prepared, settings, cancellationToken);
        if (metadata is not null)
        {
            candidates.Add(metadata);
        }

        foreach (var detector in mlDetectors)
        {
            try
            {
                var ml = await detector.AnalyzeAsync(prepared, settings, cancellationToken);
                if (ml is not null)
                {
                    candidates.Add(ml);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Detector {Detector} failed", detector.Name);
            }
        }

        var fused = Fuse(candidates, settings);
        _lastRawResult = fused;

        var smoothed = temporalSmoother.Apply(fused, settings, prepared.TimestampUtc);
        return new DetectionPipelineDecision(prepared, smoothed.Result, smoothed.ShouldEmitAlert, false);
    }

    private static DetectionResult Fuse(IReadOnlyList<DetectionResult> candidates, DetectionSettings settings)
    {
        if (candidates.Count == 0)
        {
            return DetectionResult.None();
        }

        var accepted = candidates
            .Where(candidate =>
                candidate.IsAiUiDetected
                && ((candidate.StageSource == DetectionStageSource.MetadataRule && candidate.Confidence >= settings.MetadataConfidenceThreshold)
                    || (candidate.StageSource != DetectionStageSource.MetadataRule && candidate.Confidence >= settings.MlConfidenceThreshold)))
            .OrderByDescending(static candidate => candidate.Confidence)
            .ToArray();

        if (accepted.Length == 0)
        {
            var strongestNegative = candidates.OrderByDescending(static candidate => candidate.Confidence).First();
            return strongestNegative with { IsAiUiDetected = false, Class = DetectionClass.None, IsStable = false };
        }

        if (accepted.Length == 1)
        {
            return accepted[0] with { IsStable = false };
        }

        var best = accepted[0];
        var mergedKeywords = accepted
            .SelectMany(static result => result.TriggeredKeywords ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var reason = string.Join(" + ", accepted.Select(static result => result.StageSource.ToString()));

        return best with
        {
            StageSource = DetectionStageSource.Fused,
            Reason = $"Fused: {reason}",
            TriggeredKeywords = mergedKeywords,
            IsStable = false,
        };
    }
}


