using Controledu.Detection.Abstractions;

namespace Controledu.Transport.Dto;

/// <summary>
/// Alert event emitted by student-side detection pipeline.
/// </summary>
public sealed record AlertEventDto(
    string StudentId,
    string StudentDisplayName,
    DateTimeOffset TimestampUtc,
    DetectionClass DetectionClass,
    double Confidence,
    string Reason,
    byte[]? ThumbnailJpegSmall,
    string? ModelVersion,
    string EventId,
    string StageSource,
    bool IsStable,
    string[]? TriggeredKeywords = null);
