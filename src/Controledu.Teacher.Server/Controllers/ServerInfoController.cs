using Controledu.Teacher.Server.Security;
using Controledu.Teacher.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Controledu.Teacher.Server.Controllers;

/// <summary>
/// Lightweight server info and health endpoint.
/// </summary>
[ApiController]
[Route("api/server")]
[Authorize(Policy = TeacherAuthDefaults.TeacherPolicy)]
public sealed class ServerInfoController(IServerIdentityService identityService) : ControllerBase
{
    /// <summary>
    /// Returns server identity payload.
    /// </summary>
    [HttpGet("identity")]
    public Task<Models.ServerIdentity> GetIdentity(CancellationToken cancellationToken) =>
        identityService.GetIdentityAsync(cancellationToken);

    /// <summary>
    /// Returns health status.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok", utc = DateTimeOffset.UtcNow });
}
