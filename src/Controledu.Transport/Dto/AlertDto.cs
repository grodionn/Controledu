namespace Controledu.Transport.Dto;

/// <summary>
/// Detector alert payload.
/// </summary>
public sealed record AlertDto(
    string ClientId,
    string Detector,
    string Severity,
    string Message,
    DateTimeOffset TimestampUtc,
    string? Evidence);

