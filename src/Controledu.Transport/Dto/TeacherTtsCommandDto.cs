namespace Controledu.Transport.Dto;

/// <summary>
/// Teacher-to-student TTS announcement command delivered over SignalR.
/// </summary>
public sealed record TeacherTtsCommandDto(
    string ClientId,
    string MessageText,
    string? TeacherDisplayName = null,
    string? LanguageCode = null,
    string? VoiceName = null,
    double? SpeakingRate = null,
    double? Pitch = null,
    string? RequestId = null);

