namespace Controledu.Common.Models;

/// <summary>
/// Detector result payload.
/// </summary>
public sealed record DetectionResult(
    string DetectorName,
    string Severity,
    string Message,
    DateTimeOffset Timestamp,
    string? Evidence = null);
