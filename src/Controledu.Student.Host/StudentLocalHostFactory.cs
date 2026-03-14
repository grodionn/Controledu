using Controledu.Common.Runtime;
using Controledu.Common.Security;
using Controledu.Common.Updates;
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
using Serilog;
using System.Globalization;
using System.Net;

namespace Controledu.Student.Host;

/// <summary>
/// Builds local student UI/API web host.
/// </summary>
public static partial class StudentLocalHostFactory
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

        var studentHostSection = builder.Configuration.GetSection(StudentHostOptions.SectionName);
        builder.Services
            .AddOptions<StudentHostOptions>()
            .Bind(studentHostSection)
            .Validate(static options => options.LocalPort is >= 1 and <= 65535, "StudentHost:LocalPort must be in range 1..65535.")
            .Validate(static options => !string.IsNullOrWhiteSpace(options.StorageFile), "StudentHost:StorageFile is required.")
            .Validate(static options => options.DiscoveryPort is >= 1 and <= 65535, "StudentHost:DiscoveryPort must be in range 1..65535.")
            .Validate(static options => options.DiscoveryTimeoutMs is >= 100 and <= 60_000, "StudentHost:DiscoveryTimeoutMs must be in range 100..60000.")
            .Validate(static options => !string.IsNullOrWhiteSpace(options.AgentExecutablePath), "StudentHost:AgentExecutablePath is required.")
            .Validate(static options => !string.IsNullOrWhiteSpace(options.WindowTitle), "StudentHost:WindowTitle is required.")
            .Validate(static options => options.HandRaiseCooldownSeconds is >= 1 and <= 300, "StudentHost:HandRaiseCooldownSeconds must be in range 1..300.")
            .ValidateOnStart();

        builder.Services
            .AddOptions<AutoUpdateOptions>()
            .Bind(builder.Configuration.GetSection(AutoUpdateOptions.SectionName))
            .Validate(static options => options.StartupDelaySeconds is >= 0 and <= 3600, "AutoUpdate:StartupDelaySeconds must be in range 0..3600.")
            .Validate(static options => options.CheckIntervalMinutes is >= 1 and <= 24 * 60, "AutoUpdate:CheckIntervalMinutes must be in range 1..1440.")
            .Validate(static options => options.DownloadTimeoutSeconds is >= 5 and <= 3600, "AutoUpdate:DownloadTimeoutSeconds must be in range 5..3600.")
            .Validate(static options => IsValidManifestUrl(options.ManifestUrl), "AutoUpdate:ManifestUrl must be an absolute HTTP/HTTPS URL when specified.")
            .ValidateOnStart();

        var options = studentHostSection.Get<StudentHostOptions>() ?? new StudentHostOptions();

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
        var studentChatService = scope.ServiceProvider.GetRequiredService<IStudentChatService>();
        studentChatService.ClearThreadAsync().GetAwaiter().GetResult();

        var pairingService = scope.ServiceProvider.GetRequiredService<IStudentPairingService>();
        var autoStartManager = scope.ServiceProvider.GetRequiredService<IAgentAutoStartManager>();
        var agentProcessManager = scope.ServiceProvider.GetRequiredService<IAgentProcessManager>();
        var hasBinding = pairingService.GetBindingAsync().GetAwaiter().GetResult() is not null;
        var autoStartEnabled = autoStartManager.GetEnabledAsync().GetAwaiter().GetResult();
        if (hasBinding || autoStartEnabled)
        {
            _ = agentProcessManager.StartAsync();
        }

        app.Lifetime.ApplicationStopping.Register(() =>
        {
            try
            {
                using var stopScope = app.Services.CreateScope();
                var chatService = stopScope.ServiceProvider.GetRequiredService<IStudentChatService>();
                chatService.ClearThreadAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore shutdown cleanup failures.
            }
        });

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
                || string.Equals(path, "/api/health", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, "/api/window/show", StringComparison.OrdinalIgnoreCase))
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

        app.MapPost("/api/signals/raise-hand", async (IHandRaiseRequestService handRaiseRequestService, CancellationToken cancellationToken) =>
        {
            var result = await handRaiseRequestService.RequestAsync(cancellationToken);
            if (result.Accepted)
            {
                return Results.Ok(new OkResponse(true, "Signal queued."));
            }

            return Results.Ok(new OkResponse(false, $"Rate limited. Retry after {Math.Ceiling(result.RetryAfter.TotalSeconds)}s."));
        });

        MapAccessibilityEndpoints(app);
        MapDetectionEndpoints(app);
        MapPairingEndpoints(app);
        MapAgentEndpoints(app);
        MapChatEndpoints(app);

        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapFallbackToFile("index.html");
    }

    private static bool IsValidManifestUrl(string? manifestUrl)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl))
        {
            return true;
        }

        if (!Uri.TryCreate(manifestUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);
    }
}
