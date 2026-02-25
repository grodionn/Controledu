namespace Controledu.Storage.Models;

/// <summary>
/// Immutable paired client projection.
/// </summary>
public sealed record PairedClientModel(
    string ClientId,
    string Token,
    string HostName,
    string UserName,
    string OsDescription,
    string? LocalIpAddress,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset TokenExpiresAtUtc);
