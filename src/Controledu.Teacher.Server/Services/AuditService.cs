using Controledu.Storage.Stores;

namespace Controledu.Teacher.Server.Services;

/// <summary>
/// Teacher audit log facade.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Appends audit event.
    /// </summary>
    Task RecordAsync(string action, string actor, string details, CancellationToken cancellationToken = default);
}

internal sealed class AuditService(IAuditLogStore auditLogStore) : IAuditService
{
    public Task RecordAsync(string action, string actor, string details, CancellationToken cancellationToken = default) =>
        auditLogStore.AppendAsync(action, actor, details, cancellationToken);
}
