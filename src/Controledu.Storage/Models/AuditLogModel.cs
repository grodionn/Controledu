namespace Controledu.Storage.Models;

/// <summary>
/// Immutable audit log projection.
/// </summary>
public sealed record AuditLogModel(
    long Id,
    DateTimeOffset TimestampUtc,
    string Action,
    string Actor,
    string Details);
