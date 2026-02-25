namespace Controledu.Storage.Entities;

/// <summary>
/// Audit log row.
/// </summary>
public sealed class AuditLogEntity
{
    /// <summary>
    /// Primary key.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Event timestamp.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; set; }

    /// <summary>
    /// Action name.
    /// </summary>
    public required string Action { get; set; }

    /// <summary>
    /// Event actor.
    /// </summary>
    public required string Actor { get; set; }

    /// <summary>
    /// Additional details.
    /// </summary>
    public required string Details { get; set; }
}
