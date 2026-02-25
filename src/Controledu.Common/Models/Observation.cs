namespace Controledu.Common.Models;

/// <summary>
/// Observation payload for detector pipeline.
/// </summary>
public sealed record Observation(
    DateTimeOffset Timestamp,
    string? ActiveWindowTitle,
    string? ActiveProcessName,
    string? ScreenshotReference);
