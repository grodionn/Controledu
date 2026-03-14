using Controledu.Student.Host.Contracts;
using Controledu.Student.Host.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Controledu.Student.Host;

public static partial class StudentLocalHostFactory
{
    private static void MapAgentEndpoints(WebApplication app)
    {
        app.MapPost("/api/agent/autostart", async (
            AgentAutoStartRequest request,
            IAdminPasswordService adminPasswordService,
            IAgentAutoStartManager autoStartManager,
            IAgentProcessManager agentProcessManager,
            CancellationToken cancellationToken) =>
        {
            if (!request.Enabled && !await adminPasswordService.VerifyAsync(request.AdminPassword ?? string.Empty, cancellationToken))
            {
                return Results.Unauthorized();
            }

            await autoStartManager.SetEnabledAsync(request.Enabled, cancellationToken);
            if (request.Enabled)
            {
                _ = await agentProcessManager.StartAsync(cancellationToken);
            }

            return Results.Ok(new OkResponse(true));
        });

        app.MapPost("/api/agent/start", async (IAgentProcessManager agentProcessManager, CancellationToken cancellationToken) =>
        {
            var started = await agentProcessManager.StartAsync(cancellationToken);
            return Results.Ok(new OkResponse(started, started ? "Agent started." : "Agent start failed."));
        });

        app.MapPost("/api/agent/stop", async (
            ProtectedActionRequest request,
            IAdminPasswordService adminPasswordService,
            IAgentProcessManager agentProcessManager,
            CancellationToken cancellationToken) =>
        {
            if (!await adminPasswordService.VerifyAsync(request.AdminPassword, cancellationToken))
            {
                return Results.Unauthorized();
            }

            await agentProcessManager.StopAsync(cancellationToken);
            return Results.Ok(new OkResponse(true, "Agent stopped."));
        });

        app.MapPost("/api/system/shutdown", async (
            ShutdownRequest request,
            IAdminPasswordService adminPasswordService,
            IAgentProcessManager agentProcessManager,
            IHostControlService hostControlService,
            CancellationToken cancellationToken) =>
        {
            if (!await adminPasswordService.VerifyAsync(request.AdminPassword, cancellationToken))
            {
                return Results.Unauthorized();
            }

            if (request.StopAgent)
            {
                await agentProcessManager.StopAsync(cancellationToken);
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(250, CancellationToken.None);
                hostControlService.RequestShutdown();
            });

            return Results.Ok(new OkResponse(true, "Shutdown requested."));
        });
    }
}
