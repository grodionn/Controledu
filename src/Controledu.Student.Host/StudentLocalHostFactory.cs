using Controledu.Common.Runtime;
using Controledu.Common.Security;
using Controledu.Discovery.Services;
using Controledu.Student.Host.Contracts;
using Controledu.Student.Host.Options;
using Controledu.Student.Host.Services;
using Controledu.Storage.Extensions;
using Controledu.Storage.Stores;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using System.Globalization;
using System.Net;

namespace Controledu.Student.Host;

/// <summary>
/// Builds local student UI/API web host.
/// </summary>
public static class StudentLocalHostFactory
{
    private const string LocalApiTokenHeader = "X-Controledu-LocalToken";

    /// <summary>
    /// Creates configured local web application.
    /// </summary>
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args);

        builder.Services.Configure<StudentHostOptions>(builder.Configuration.GetSection(StudentHostOptions.SectionName));
        var options = builder.Configuration.GetSection(StudentHostOptions.SectionName).Get<StudentHostOptions>() ?? new StudentHostOptions();

        var storagePath = Path.IsPathRooted(options.StorageFile)
            ? options.StorageFile
            : Path.Combine(AppPaths.GetBasePath(), options.StorageFile);

        builder.Services.AddControleduStorage(storagePath);
        builder.Services.AddSingleton<ISecretProtector>(_ => SecretProtectorFactory.CreateDefault());

        builder.Services.Configure<DiscoveryOptions>(discoveryOptions =>
        {
            discoveryOptions.DiscoveryPort = options.DiscoveryPort;
            discoveryOptions.ProbeTimeoutMs = options.DiscoveryTimeoutMs;
        });

        builder.Services.AddSingleton<UdpDiscoveryClient>();
        builder.Services.AddHttpClient();

        builder.Services.AddSingleton<ILocalSessionTokenProvider, LocalSessionTokenProvider>();
        builder.Services.AddSingleton<IAdminPasswordService, AdminPasswordService>();
        builder.Services.AddSingleton<IDeviceIdentityService, DeviceIdentityService>();
        builder.Services.AddSingleton<IStudentPairingService, StudentPairingService>();
        builder.Services.AddSingleton<IAgentAutoStartManager, AgentAutoStartManager>();
        builder.Services.AddSingleton<IAgentProcessManager, AgentProcessManager>();
        builder.Services.AddSingleton<IAccessibilitySettingsService, AccessibilitySettingsService>();
        builder.Services.AddSingleton<IStudentStatusService, StudentStatusService>();
        builder.Services.AddSingleton<IDetectionLocalService, DetectionLocalService>();
        builder.Services.AddSingleton<IHostControlService, HostControlService>();
        builder.Services.AddSingleton<IHandRaiseRequestService, HandRaiseRequestService>();
        builder.Services.AddSingleton<IStudentChatService, StudentChatService>();
        builder.Services.AddSingleton<IStudentLiveCaptionService, StudentLiveCaptionService>();
        builder.Services.AddSingleton<IRemoteControlConsentService, RemoteControlConsentService>();

        builder.Host.UseSerilog((context, _, loggerConfiguration) =>
        {
            loggerConfiguration
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
                .WriteTo.File(Path.Combine(AppPaths.GetLogsPath(), "student-host-.log"), rollingInterval: RollingInterval.Day, formatProvider: CultureInfo.InvariantCulture);
        });

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ListenLocalhost(options.LocalPort);
        });

        var app = builder.Build();
        ConfigurePipeline(app);

        using var scope = app.Services.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<IStorageInitializer>();
        initializer.EnsureCreatedAsync().GetAwaiter().GetResult();

        var pairingService = scope.ServiceProvider.GetRequiredService<IStudentPairingService>();
        var autoStartManager = scope.ServiceProvider.GetRequiredService<IAgentAutoStartManager>();
        var agentProcessManager = scope.ServiceProvider.GetRequiredService<IAgentProcessManager>();
        var hasBinding = pairingService.GetBindingAsync().GetAwaiter().GetResult() is not null;
        var autoStartEnabled = autoStartManager.GetEnabledAsync().GetAwaiter().GetResult();
        if (hasBinding || autoStartEnabled)
        {
            _ = agentProcessManager.StartAsync();
        }

        return app;
    }

    private static void ConfigurePipeline(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            if (context.Connection.RemoteIpAddress is not null && !IPAddress.IsLoopback(context.Connection.RemoteIpAddress))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Local API is available only on loopback interface.");
                return;
            }

            var path = context.Request.Path.Value ?? string.Empty;
            if (string.Equals(path, "/api/session", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, "/api/health", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            if (string.Equals(path, "/api/window/show", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var tokenProvider = context.RequestServices.GetRequiredService<ILocalSessionTokenProvider>();
            var token = context.Request.Headers[LocalApiTokenHeader].FirstOrDefault();
            if (!string.Equals(token, tokenProvider.Token, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Invalid local API token.");
                return;
            }

            await next();
        });

        app.MapGet("/api/health", () => Results.Ok(new { status = "ok", utc = DateTimeOffset.UtcNow }));

        app.MapGet("/api/session", (ILocalSessionTokenProvider tokenProvider) =>
            Results.Ok(new SessionTokenResponse(tokenProvider.Token)));

        app.MapPost("/api/window/show", (IHostControlService hostControlService) =>
        {
            hostControlService.RequestShow();
            return Results.Ok(new OkResponse(true, "Show requested."));
        });

        app.MapGet("/api/status", async (IStudentStatusService statusService, CancellationToken cancellationToken) =>
        {
            var status = await statusService.GetAsync(cancellationToken);
            return Results.Ok(status);
        });

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

        app.MapGet("/api/detection/status", async (IDetectionLocalService detectionLocalService, CancellationToken cancellationToken) =>
        {
            var status = await detectionLocalService.GetStatusAsync(cancellationToken);
            return Results.Ok(status);
        });

        app.MapGet("/api/device-name", async (IDeviceIdentityService deviceIdentityService, CancellationToken cancellationToken) =>
        {
            var name = await deviceIdentityService.GetDisplayNameAsync(cancellationToken);
            return Results.Ok(new DeviceNameResponse(name));
        });

        app.MapPost("/api/admin/verify", async (
            ProtectedActionRequest request,
            IAdminPasswordService adminPasswordService,
            CancellationToken cancellationToken) =>
        {
            if (!await adminPasswordService.VerifyAsync(request.AdminPassword, cancellationToken))
            {
                return Results.Unauthorized();
            }

            return Results.Ok(new OkResponse(true));
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

            await detectionLocalService.UpdateLocalConfigAsync(request, cancellationToken);
            return Results.Ok(new OkResponse(true, "Detection local config updated."));
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

        app.MapPost("/api/device-name", async (
            DeviceNameUpdateRequest request,
            IAdminPasswordService adminPasswordService,
            IDeviceIdentityService deviceIdentityService,
            IAgentProcessManager agentProcessManager,
            CancellationToken cancellationToken) =>
        {
            if (!await adminPasswordService.VerifyAsync(request.AdminPassword, cancellationToken))
            {
                return Results.Unauthorized();
            }

            try
            {
                await deviceIdentityService.SetDisplayNameAsync(request.DeviceName, cancellationToken);
                await agentProcessManager.StopAsync(cancellationToken);
                _ = await agentProcessManager.StartAsync(cancellationToken);
                return Results.Ok(new OkResponse(true, "Device name updated."));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        app.MapPost("/api/setup/admin-password", async (
            SetupAdminPasswordRequest request,
            IAdminPasswordService adminPasswordService,
            IAgentAutoStartManager autoStartManager,
            IAgentProcessManager agentProcessManager,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await adminPasswordService.SetInitialPasswordAsync(request.Password, request.ConfirmPassword, cancellationToken);
                await autoStartManager.SetEnabledAsync(request.EnableAgentAutoStart, cancellationToken);

                if (request.EnableAgentAutoStart)
                {
                    _ = await agentProcessManager.StartAsync(cancellationToken);
                }

                return Results.Ok(new OkResponse(true, "Admin password set."));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

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

        app.MapPost("/api/signals/raise-hand", async (IHandRaiseRequestService handRaiseRequestService, CancellationToken cancellationToken) =>
        {
            var result = await handRaiseRequestService.RequestAsync(cancellationToken);
            if (result.Accepted)
            {
                return Results.Ok(new OkResponse(true, "Signal queued."));
            }

            return Results.Ok(new OkResponse(false, $"Rate limited. Retry after {Math.Ceiling(result.RetryAfter.TotalSeconds)}s."));
        });

        app.MapGet("/api/chat/thread", async (IStudentChatService studentChatService, CancellationToken cancellationToken) =>
        {
            var thread = await studentChatService.GetThreadAsync(cancellationToken);
            return Results.Ok(thread);
        });

        app.MapPost("/api/chat/messages", async (
            StudentChatSendRequest request,
            IStudentChatService studentChatService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var message = await studentChatService.QueueStudentMessageAsync(request, cancellationToken);
                return Results.Ok(message);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        app.MapPost("/api/chat/messages/teacher", async (
            TeacherChatLocalDeliveryRequest request,
            IStudentChatService studentChatService,
            CancellationToken cancellationToken) =>
        {
            var saved = await studentChatService.ReceiveTeacherMessageAsync(request, cancellationToken);
            return saved is null ? Results.BadRequest("Teacher chat message text is required.") : Results.Ok(saved);
        });

        app.MapPost("/api/chat/outgoing/peek", async (IStudentChatService studentChatService, CancellationToken cancellationToken) =>
        {
            var messages = await studentChatService.PeekOutgoingAsync(cancellationToken);
            return Results.Ok(new StudentChatOutboxPeekResponse(messages));
        });

        app.MapPost("/api/chat/outgoing/ack", async (
            StudentChatOutboxAckRequest request,
            IStudentChatService studentChatService,
            CancellationToken cancellationToken) =>
        {
            var removed = await studentChatService.AcknowledgeOutgoingAsync(request.MessageIds ?? [], cancellationToken);
            return Results.Ok(new OkResponse(true, $"Ack removed {removed} message(s)."));
        });

        app.MapPost("/api/chat/preferences", async (
            StudentChatPreferencesUpdateRequest request,
            IStudentChatService studentChatService,
            CancellationToken cancellationToken) =>
        {
            var prefs = await studentChatService.UpdatePreferencesAsync(request, cancellationToken);
            return Results.Ok(prefs);
        });

        app.MapGet("/api/captions/live", async (IStudentLiveCaptionService studentLiveCaptionService, CancellationToken cancellationToken) =>
        {
            var caption = await studentLiveCaptionService.GetCurrentAsync(cancellationToken);
            return Results.Ok(caption);
        });

        app.MapPost("/api/captions/live/teacher", async (
            TeacherLiveCaptionLocalDeliveryRequest request,
            IStudentLiveCaptionService studentLiveCaptionService,
            CancellationToken cancellationToken) =>
        {
            var caption = await studentLiveCaptionService.ApplyTeacherCaptionAsync(request, cancellationToken);
            return Results.Ok(caption);
        });

        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapFallbackToFile("index.html");
    }
}
