using Controledu.Transport.Dto;
using Controledu.Storage.Models;
using Controledu.Storage.Stores;
using Controledu.Teacher.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Controledu.Teacher.Server.Controllers;

/// <summary>
/// Pairing API endpoints for student onboarding.
/// </summary>
[ApiController]
[Route("api/pairing")]
public sealed class PairingController(
    IPairingCodeService pairingCodeService,
    IServerIdentityService identityService,
    IPairedClientStore pairedClientStore,
    IAuditService auditService,
    IOptions<Options.TeacherServerOptions> options,
    ISystemClock clock) : ControllerBase
{
    /// <summary>
    /// Generates one-time pairing PIN.
    /// </summary>
    [HttpPost("pin")]
    [ProducesResponseType<PairingPinDto>(StatusCodes.Status200OK)]
    public ActionResult<PairingPinDto> GeneratePin() => Ok(pairingCodeService.Generate());

    /// <summary>
    /// Completes student pairing using one-time PIN.
    /// </summary>
    [HttpPost("complete")]
    [ProducesResponseType<PairingResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PairingResponseDto>> CompletePairing([FromBody] PairingRequestDto request, CancellationToken cancellationToken)
    {
        if (!pairingCodeService.TryConsume(request.PinCode))
        {
            return BadRequest("PIN invalid or expired.");
        }

        var identity = await identityService.GetIdentityAsync(cancellationToken);
        var clientId = Guid.NewGuid().ToString("N");
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var tokenExpiresAtUtc = clock.UtcNow.AddHours(Math.Max(1, options.Value.PairingTokenLifetimeHours));

        var client = new PairedClientModel(
            clientId,
            token,
            request.HostName,
            request.UserName,
            request.OsDescription,
            request.LocalIpAddress,
            clock.UtcNow,
            tokenExpiresAtUtc);

        await pairedClientStore.UpsertAsync(client, cancellationToken);
        await auditService.RecordAsync("device_paired", clientId, $"{request.HostName} ({request.UserName})", cancellationToken);

        var response = new PairingResponseDto(
            identity.ServerId,
            identity.ServerName,
            GetServerBaseUrl(),
            identity.Fingerprint,
            clientId,
            token,
            tokenExpiresAtUtc);

        return Ok(response);
    }

    private string GetServerBaseUrl()
    {
        if (Request.Host.HasValue)
        {
            return $"{Request.Scheme}://{Request.Host.Value}";
        }

        return $"http://127.0.0.1:{options.Value.HttpPort}";
    }
}

