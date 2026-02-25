using Controledu.Storage.Models;
using Controledu.Storage.Stores;
using Microsoft.AspNetCore.Mvc;

namespace Controledu.Teacher.Server.Controllers;

/// <summary>
/// Read-only audit log API.
/// </summary>
[ApiController]
[Route("api/audit")]
public sealed class AuditController(IAuditLogStore auditLogStore) : ControllerBase
{
    /// <summary>
    /// Returns latest audit events.
    /// </summary>
    [HttpGet("latest")]
    [ProducesResponseType<IReadOnlyList<AuditLogModel>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<AuditLogModel>> Latest([FromQuery] int take = 100, CancellationToken cancellationToken = default)
    {
        var boundedTake = Math.Clamp(take, 1, 500);
        return auditLogStore.GetLatestAsync(boundedTake, cancellationToken);
    }
}
