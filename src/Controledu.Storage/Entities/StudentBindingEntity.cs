namespace Controledu.Storage.Entities;

/// <summary>
/// Local student binding to teacher server.
/// </summary>
public sealed class StudentBindingEntity
{
    /// <summary>
    /// Primary key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Teacher server identifier.
    /// </summary>
    public required string ServerId { get; set; }

    /// <summary>
    /// Teacher server display name.
    /// </summary>
    public required string ServerName { get; set; }

    /// <summary>
    /// Base URL of teacher server.
    /// </summary>
    public required string ServerBaseUrl { get; set; }

    /// <summary>
    /// Server certificate fingerprint placeholder.
    /// </summary>
    public required string ServerFingerprint { get; set; }

    /// <summary>
    /// Student client identifier.
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// Protected token payload in base64.
    /// </summary>
    public required string ProtectedTokenBase64 { get; set; }

    /// <summary>
    /// Last update timestamp.
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
