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
    private const double MlMetadataGateThresholdFloor = 0.35;
    private const double WeakMetadataSignatureConfidence = 0.40;
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

        var strongestNegative = candidates
            .Where(static candidate => !candidate.IsAiUiDetected)
            .OrderByDescending(static candidate => candidate.Confidence)
            .FirstOrDefault();

        var accepted = candidates
            .Where(candidate =>
                candidate.IsAiUiDetected
                && ((candidate.StageSource == DetectionStageSource.MetadataRule && candidate.Confidence >= settings.MetadataConfidenceThreshold)
                    || (candidate.StageSource != DetectionStageSource.MetadataRule && candidate.Confidence >= settings.MlConfidenceThreshold)))
            .OrderByDescending(static candidate => candidate.Confidence)
            .ToArray();

        if (accepted.Length == 0)
        {
            if (strongestNegative is not null)
            {
                return strongestNegative with { IsAiUiDetected = false, Class = DetectionClass.None, IsStable = false };
            }

            return DetectionResult.None();
        }

        var mlMetadataGateThreshold = Math.Min(settings.MetadataConfidenceThreshold, MlMetadataGateThresholdFloor);
        var strongestMetadataSignal = candidates
            .Where(static candidate => candidate.StageSource == DetectionStageSource.MetadataRule)
            .Select(GetMetadataSignalStrength)
            .DefaultIfEmpty(0d)
            .Max();
        var hasAcceptedMlPositive = accepted.Any(candidate =>
            candidate.StageSource is DetectionStageSource.OnnxBinary or DetectionStageSource.OnnxMulticlass);

        if (hasAcceptedMlPositive && strongestMetadataSignal < mlMetadataGateThreshold)
        {
            if (strongestNegative is not null)
            {
                return strongestNegative with
                {
                    IsAiUiDetected = false,
                    Class = DetectionClass.None,
                    Reason = "Suppressed: metadata signature required for ML verdict.",
                    IsStable = false,
                };
            }

            return DetectionResult.None("Suppressed: metadata signature required for ML verdict.");
        }

        var best = accepted[0];
        var hasOnlyUnknownPositives = accepted.All(static candidate => candidate.Class == DetectionClass.UnknownAi);
        var strongNotAiMulticlass = candidates
            .Where(candidate =>
                !candidate.IsAiUiDetected
                && candidate.StageSource == DetectionStageSource.OnnxMulticlass
                && candidate.Class == DetectionClass.None
                && candidate.Confidence >= settings.MlConfidenceThreshold)
            .OrderByDescending(static candidate => candidate.Confidence)
            .FirstOrDefault();

        if (hasOnlyUnknownPositives
            && strongNotAiMulticlass is not null
            && strongNotAiMulticlass.Confidence + 0.03 >= best.Confidence)
        {
            return strongNotAiMulticlass with
            {
                IsAiUiDetected = false,
                Class = DetectionClass.None,
                Reason = "Suppressed by ONNX multiclass not_ai_ui signal.",
                IsStable = false,
            };
        }

        var strongestSpecific = accepted
            .Where(static candidate => candidate.Class != DetectionClass.None && candidate.Class != DetectionClass.UnknownAi)
            .OrderByDescending(static candidate => candidate.Confidence)
            .FirstOrDefault();
        var resolvedClass = strongestSpecific?.Class ?? best.Class;
        var classResolvedByAnotherStage = strongestSpecific is not null && strongestSpecific.Class != best.Class;

        if (accepted.Length == 1)
        {
            var singleReason = classResolvedByAnotherStage
                ? $"{best.Reason} | class from {strongestSpecific!.StageSource}"
                : best.Reason;
            return best with
            {
                Class = resolvedClass,
                Reason = singleReason,
                IsStable = false,
            };
        }

        var mergedKeywords = accepted
            .SelectMany(static result => result.TriggeredKeywords ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var reason = string.Join(" + ", accepted.Select(static result => result.StageSource.ToString()));
        if (classResolvedByAnotherStage)
        {
            reason = $"{reason} | class from {strongestSpecific!.StageSource}";
        }

        return best with
        {
            Class = resolvedClass,
            StageSource = DetectionStageSource.Fused,
            Reason = $"Fused: {reason}",
            TriggeredKeywords = mergedKeywords,
            IsStable = false,
        };
    }

    private static double GetMetadataSignalStrength(DetectionResult candidate)
    {
        if (candidate.IsAiUiDetected)
        {
            return candidate.Confidence;
        }

        // Weak metadata signature: keyword matched in one field only.
        if (candidate.TriggeredKeywords is { Count: > 0 }
            && candidate.Reason.Contains("insufficient evidence", StringComparison.OrdinalIgnoreCase))
        {
            return WeakMetadataSignatureConfidence;
        }

        return 0;
    }
}


