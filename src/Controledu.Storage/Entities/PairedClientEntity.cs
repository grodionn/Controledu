namespace Controledu.Storage.Entities;

/// <summary>
/// Teacher-side accepted paired client.
/// </summary>
public sealed class PairedClientEntity
{
    /// <summary>
    /// Primary key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Stable client identifier.
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// Authentication token.
    /// </summary>
    public required string Token { get; set; }

    /// <summary>
    /// Device host name.
    /// </summary>
    public required string HostName { get; set; }

    /// <summary>
    /// User name.
    /// </summary>
    public required string UserName { get; set; }

    /// <summary>
    /// OS description.
    /// </summary>
    public required string OsDescription { get; set; }

    /// <summary>
    /// Last known local IP address.
    /// </summary>
    public string? LocalIpAddress { get; set; }

    /// <summary>
    /// Record creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// Token expiry.
    /// </summary>
    public DateTimeOffset TokenExpiresAtUtc { get; set; }
}
