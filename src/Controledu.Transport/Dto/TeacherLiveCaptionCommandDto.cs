namespace Controledu.Transport.Dto;

/// <summary>
/// Teacher-to-student live caption update delivered over SignalR for endpoint overlay subtitles.
/// </summary>
public sealed record TeacherLiveCaptionCommandDto(
    string ClientId,
    string CaptionId,
    DateTimeOffset TimestampUtc,
    string TeacherDisplayName,
    string? LanguageCode,
    string Text,
    bool IsFinal,
    bool Clear,
    int TtlMs,
    long Sequence);
