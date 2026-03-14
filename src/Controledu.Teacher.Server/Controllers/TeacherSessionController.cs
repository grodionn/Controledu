using Controledu.Teacher.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace Controledu.Teacher.Server.Controllers;

/// <summary>
/// Returns short-lived teacher UI session bootstrap payload.
/// </summary>
[ApiController]
[Route("api/session")]
public sealed class TeacherSessionController(ITeacherApiTokenProvider tokenProvider) : ControllerBase
{
    /// <summary>
    /// Returns teacher API token for local teacher UI runtime.
    /// </summary>
    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetToken(CancellationToken cancellationToken)
    {
        if (HttpContext.Connection.RemoteIpAddress is { } remoteIp && !IPAddress.IsLoopback(remoteIp))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Session token is available only from loopback." });
        }

        var token = await tokenProvider.GetTokenAsync(cancellationToken);
        return Ok(new TeacherSessionResponse(token));
    }
}

/// <summary>
/// Teacher UI bootstrap session payload.
/// </summary>
public sealed record TeacherSessionResponse(string Token);
