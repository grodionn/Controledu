namespace Controledu.Transport.Dto;

/// <summary>
/// Interactive student signal event delivered to teacher clients.
/// </summary>
public sealed record StudentSignalEventDto(
    string StudentId,
    string StudentDisplayName,
    StudentSignalType SignalType,
    DateTimeOffset TimestampUtc,
    string EventId,
    string? Message = null);

