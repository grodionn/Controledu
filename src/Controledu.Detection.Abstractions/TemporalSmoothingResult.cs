namespace Controledu.Detection.Abstractions;

/// <summary>
/// Temporal smoothing output.
/// </summary>
public sealed record TemporalSmoothingResult(
    DetectionResult Result,
    bool ShouldEmitAlert);
