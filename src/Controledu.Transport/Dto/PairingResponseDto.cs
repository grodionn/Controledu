namespace Controledu.Transport.Dto;

/// <summary>
/// Pairing response returned by teacher server.
/// </summary>
public sealed record PairingResponseDto(
    string ServerId,
    string ServerName,
    string ServerBaseUrl,
    string ServerFingerprint,
    string ClientId,
    string Token,
    DateTimeOffset TokenExpiresAtUtc);

