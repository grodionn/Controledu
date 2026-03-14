using Controledu.Student.Host.Contracts;
using Controledu.Student.Host.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Controledu.Student.Host;

public static partial class StudentLocalHostFactory
{
    private static void MapAccessibilityEndpoints(WebApplication app)
    {
        app.MapGet("/api/accessibility/profile", async (IAccessibilitySettingsService accessibilitySettingsService, CancellationToken cancellationToken) =>
        {
            var profile = await accessibilitySettingsService.GetAsync(cancellationToken);
            return Results.Ok(profile);
        });

        app.MapPost("/api/accessibility/profile", async (
            AccessibilityProfileUpdateRequest request,
            IAccessibilitySettingsService accessibilitySettingsService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var profile = await accessibilitySettingsService.UpdateFromLocalAsync(request, cancellationToken);
                return Results.Ok(profile);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        app.MapPost("/api/accessibility/profile/preset", async (
            AccessibilityPresetApplyRequest request,
            IAccessibilitySettingsService accessibilitySettingsService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var profile = await accessibilitySettingsService.ApplyPresetAsync(request.PresetId, cancellationToken);
                return Results.Ok(profile);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        app.MapPost("/api/accessibility/profile/teacher-assign", async (
            TeacherAssignedAccessibilityProfileRequest request,
            IAccessibilitySettingsService accessibilitySettingsService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var profile = await accessibilitySettingsService.ApplyTeacherAssignedAsync(request, cancellationToken);
                return Results.Ok(profile);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new OkResponse(false, ex.Message));
            }
        });
    }
}
