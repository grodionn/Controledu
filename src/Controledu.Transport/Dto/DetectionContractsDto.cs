using Controledu.Detection.Abstractions;

namespace Controledu.Transport.Dto;

/// <summary>
/// Shared detection observation DTO for diagnostics and dataset metadata.
/// </summary>
public sealed record DetectionObservationDto(
    string StudentId,
    DateTimeOffset TimestampUtc,
    string? ScreenFrameHash,
    bool FrameChanged,
    string? ActiveProcessName,
    string? ActiveWindowTitle,
    string? BrowserHintUrl,
    string? OptionalScreenshotPath,
    byte[]? OptionalThumbnailBytes);

/// <summary>
/// Shared detection result DTO for diagnostics and telemetry.
/// </summary>
public sealed record DetectionResultDto(
    bool IsAiUiDetected,
    double Confidence,
    DetectionClass Class,
    string StageSource,
    string Reason,
    string? ModelVersion,
    string[]? TriggeredKeywords,
    bool IsStable);
