namespace Controledu.Detection.Abstractions;

/// <summary>
/// Detection result produced by one stage or the fused pipeline.
/// </summary>
public sealed record DetectionResult(
    bool IsAiUiDetected,
    double Confidence,
    DetectionClass Class,
    DetectionStageSource StageSource,
    string Reason,
    string? ModelVersion,
    IReadOnlyList<string>? TriggeredKeywords,
    bool IsStable)
{
    /// <summary>
    /// Creates a negative result.
    /// </summary>
    public static DetectionResult None(string reason = "No AI UI signals detected.") =>
        new(false, 0, DetectionClass.None, DetectionStageSource.None, reason, null, null, false);
}
