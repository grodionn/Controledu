namespace Controledu.Transport.Dto;

/// <summary>
/// Student info projection for teacher UI.
/// </summary>
public sealed record StudentInfoDto(
    string ClientId,
    string HostName,
    string UserName,
    string? LocalIpAddress,
    DateTimeOffset LastSeenUtc,
    bool IsOnline,
    bool DetectionEnabled = true,
    DateTimeOffset? LastDetectionAtUtc = null,
    string? LastDetectionClass = null,
    double? LastDetectionConfidence = null,
    int AlertCount = 0);

