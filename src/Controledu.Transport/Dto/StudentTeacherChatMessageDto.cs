namespace Controledu.Transport.Dto;

/// <summary>
/// Teacher-student chat message transported between server, teacher UI, and student agent.
/// </summary>
public sealed record StudentTeacherChatMessageDto(
    string ClientId,
    string MessageId,
    DateTimeOffset TimestampUtc,
    string SenderRole,
    string SenderDisplayName,
    string Text);

