using Controledu.Student.Host.Contracts;
using Controledu.Student.Host.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Controledu.Student.Host;

public static partial class StudentLocalHostFactory
{
    private static void MapDetectionEndpoints(WebApplication app)
    {
        app.MapGet("/api/detection/status", async (IDetectionLocalService detectionLocalService, CancellationToken cancellationToken) =>
        {
            var status = await detectionLocalService.GetStatusAsync(cancellationToken);
            return Results.Ok(status);
        });

        app.MapPost("/api/detection/config", async (
            DetectionConfigUpdateRequest request,
            IAdminPasswordService adminPasswordService,
            IDetectionLocalService detectionLocalService,
            CancellationToken cancellationToken) =>
        {
            if (!await adminPasswordService.VerifyAsync(request.AdminPassword, cancellationToken))
            {
                return Results.Unauthorized();
            }

            try
            {
                await detectionLocalService.UpdateLocalConfigAsync(request, cancellationToken);
                return Results.Ok(new OkResponse(true, "Detection local config updated."));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new OkResponse(false, ex.Message));
            }
        });

        app.MapPost("/api/detection/self-test", async (
            DetectionSelfTestRequest request,
            IAdminPasswordService adminPasswordService,
            IDetectionLocalService detectionLocalService,
            CancellationToken cancellationToken) =>
        {
            if (!await adminPasswordService.VerifyAsync(request.AdminPassword, cancellationToken))
            {
                return Results.Unauthorized();
            }

            await detectionLocalService.TriggerSelfTestAsync(cancellationToken);
            return Results.Ok(new OkResponse(true, "Self-test request scheduled."));
        });

        app.MapPost("/api/detection/export-diagnostics", async (
            ProtectedActionRequest request,
            IAdminPasswordService adminPasswordService,
            IDetectionLocalService detectionLocalService,
            CancellationToken cancellationToken) =>
        {
            if (!await adminPasswordService.VerifyAsync(request.AdminPassword, cancellationToken))
            {
                return Results.Unauthorized();
            }

            var archivePath = await detectionLocalService.ExportDiagnosticsAsync(cancellationToken);
            return Results.Ok(new DiagnosticsExportResponse(archivePath));
        });
    }
}
