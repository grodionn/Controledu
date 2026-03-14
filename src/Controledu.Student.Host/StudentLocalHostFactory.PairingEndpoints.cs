using Controledu.Student.Host.Contracts;
using Controledu.Student.Host.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Controledu.Student.Host;

public static partial class StudentLocalHostFactory
{
    private static void MapPairingEndpoints(WebApplication app)
    {
        app.MapPost("/api/discovery", async (DiscoveryRequest? request, IStudentPairingService pairingService, CancellationToken cancellationToken) =>
        {
            try
            {
                var discovered = await pairingService.DiscoverAsync(request?.TimeoutMs, cancellationToken);
                var payload = discovered.Select((server, index) => DiscoveredServerResponse.FromDto(server, index == 0)).ToArray();
                return Results.Ok(payload);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        app.MapPost("/api/pairing", async (
            PairingRequest request,
            IAdminPasswordService adminPasswordService,
            IStudentPairingService pairingService,
            IAgentProcessManager agentProcessManager,
            CancellationToken cancellationToken) =>
        {
            if (!await adminPasswordService.HasPasswordAsync(cancellationToken))
            {
                return Results.BadRequest("Admin password is not configured.");
            }

            try
            {
                await pairingService.PairAsync(request.Pin, request.ServerAddress, cancellationToken);
                _ = await agentProcessManager.StartAsync(cancellationToken);
                return Results.Ok(new OkResponse(true, "Pairing completed."));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        app.MapPost("/api/unpair", async (
            UnpairRequest request,
            IAdminPasswordService adminPasswordService,
            IStudentPairingService pairingService,
            IAgentProcessManager agentProcessManager,
            CancellationToken cancellationToken) =>
        {
            if (!await adminPasswordService.VerifyAsync(request.AdminPassword, cancellationToken))
            {
                return Results.Unauthorized();
            }

            await pairingService.ClearBindingAsync(cancellationToken);
            await agentProcessManager.StopAsync(cancellationToken);
            return Results.Ok(new OkResponse(true, "Unpaired."));
        });
    }
}
