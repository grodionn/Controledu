namespace Controledu.Storage.Models;

/// <summary>
/// Immutable projection of student binding info.
/// </summary>
public sealed record StudentBindingModel(
    string ServerId,
    string ServerName,
    string ServerBaseUrl,
    string ServerFingerprint,
    string ClientId,
    string ProtectedTokenBase64,
    DateTimeOffset UpdatedAtUtc);
