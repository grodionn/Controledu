namespace Controledu.Detection.Abstractions;

/// <summary>
/// Observation payload passed into the AI detection pipeline.
/// </summary>
public sealed record DetectionObservation
{
    /// <summary>
    /// Stable student device identifier.
    /// </summary>
    public required string StudentId { get; init; }

    /// <summary>
    /// UTC timestamp of the observation.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Perceptual screen hash (filled by frame-change stage).
    /// </summary>
    public string? ScreenFrameHash { get; init; }

    /// <summary>
    /// Indicates whether the frame changed materially.
    /// </summary>
    public bool FrameChanged { get; init; }

    /// <summary>
    /// Active process name when available.
    /// </summary>
    public string? ActiveProcessName { get; init; }

    /// <summary>
    /// Active window title when available.
    /// </summary>
    public string? ActiveWindowTitle { get; init; }

    /// <summary>
    /// Optional browser URL/domain hint.
    /// </summary>
    public string? BrowserHintUrl { get; init; }

    /// <summary>
    /// Optional captured screenshot path for dataset collection mode.
    /// </summary>
    public string? OptionalScreenshotPath { get; init; }

    /// <summary>
    /// Optional thumbnail payload for alert preview.
    /// </summary>
    public byte[]? OptionalThumbnailBytes { get; init; }

    /// <summary>
    /// Optional full frame bytes used by ML detectors.
    /// </summary>
    public byte[]? FrameBytes { get; init; }
}
