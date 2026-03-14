using Controledu.Common.Runtime;
using Controledu.Storage.Extensions;
using Controledu.Storage.Stores;
using Controledu.Teacher.Server.Hubs;
using Controledu.Teacher.Server.Options;
using Controledu.Teacher.Server.Security;
using Controledu.Teacher.Server.Services;
using Controledu.Transport.Constants;
using Microsoft.Extensions.Options;
using Serilog;
using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;

namespace Controledu.Teacher.Server;

/// <summary>
/// Factory for standalone and embedded teacher server host.
/// </summary>
public static class TeacherServerHostFactory
{
    private const string CorsPolicyName = "TeacherCorsPolicy";

    /// <summary>
    /// Builds configured web application instance.
    /// </summary>
    public static WebApplication Build(string[]? args = null, Action<WebApplicationBuilder>? configureBuilder = null)
    {
        var builder = WebApplication.CreateBuilder(args ?? []);
        configureBuilder?.Invoke(builder);
        ConfigureBuilder(builder);

        var app = builder.Build();
        ConfigureApp(app);
        return app;
    }

    /// <summary>
    /// Configures dependency injection and host settings.
    /// </summary>
    public static void ConfigureBuilder(WebApplicationBuilder builder)
    {
        builder.Services
            .AddOptions<TeacherServerOptions>()
            .Bind(builder.Configuration.GetSection(TeacherServerOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(static options => IsValidTeacherApiToken(options.TeacherApiToken),
                "TeacherServer:TeacherApiToken must be at least 16 characters if specified.")
            .Validate(static options => IsValidCorsOrigins(options.AllowedCorsOrigins),
                "TeacherServer:AllowedCorsOrigins must contain absolute HTTP/HTTPS origins.")
            .ValidateOnStart();

        var options = builder.Configuration.GetSection(TeacherServerOptions.SectionName).Get<TeacherServerOptions>() ?? new TeacherServerOptions();
        var sqlitePath = ResolvePath(options.StorageFile, "teacher-server.db");
        var configuredCorsOrigins = BuildConfiguredCorsOriginSet(options.AllowedCorsOrigins);

        builder.Services.AddControleduStorage(sqlitePath);
        builder.Services
            .AddControllers()
            .AddJsonOptions(json =>
            {
                json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            })
            .AddApplicationPart(typeof(TeacherServerAssemblyMarker).Assembly);

        builder.Services.AddSignalR(hubOptions =>
        {
            hubOptions.MaximumReceiveMessageSize = Math.Max(32L * 1024L, options.SignalRMaxReceiveMessageBytes);
        })
        .AddJsonProtocol(json =>
        {
            json.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        builder.Services
            .AddAuthentication(TeacherAuthDefaults.AuthenticationScheme)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TeacherApiTokenAuthenticationHandler>(
                TeacherAuthDefaults.AuthenticationScheme,
                static _ =>
                {
                });

        builder.Services.AddAuthorization(authorization =>
        {
            authorization.AddPolicy(TeacherAuthDefaults.TeacherPolicy, policy =>
            {
                policy.AddAuthenticationSchemes(TeacherAuthDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
            });
        });

        builder.Services.AddHttpClient();
        builder.Services.AddCors(cors =>
        {
            cors.AddPolicy(CorsPolicyName, policy =>
            {
                policy
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials()
                    .SetIsOriginAllowed(origin => IsAllowedCorsOrigin(origin, configuredCorsOrigins));
            });
        });

        builder.Services.AddSingleton<ISystemClock, SystemClock>();
        builder.Services.AddSingleton<ITeacherApiTokenProvider, TeacherApiTokenProvider>();
        builder.Services.AddSingleton<IPairingCodeService, PairingCodeService>();
        builder.Services.AddSingleton<IServerIdentityService, ServerIdentityService>();
        builder.Services.AddSingleton<IStudentRegistry, StudentRegistry>();
        builder.Services.AddSingleton<IFileTransferCoordinator, FileTransferCoordinator>();
        builder.Services.AddSingleton<IAuditService, AuditService>();
        builder.Services.AddSingleton<IDesktopNotificationService, DesktopNotificationService>();
        builder.Services.AddSingleton<IDetectionPolicyService, DetectionPolicyService>();
        builder.Services.AddSingleton<IDetectionEventStore, DetectionEventStore>();
        builder.Services.AddSingleton<IStudentSignalGate, StudentSignalGate>();
        builder.Services.AddSingleton<IStudentChatService, StudentChatService>();
        builder.Services.AddSingleton<IRemoteControlSessionService, RemoteControlSessionService>();
        builder.Services.AddSingleton<IHostControlService, HostControlService>();
        builder.Services.AddHostedService<UdpDiscoveryResponderService>();

        builder.WebHost.ConfigureKestrel((context, kestrel) =>
        {
            var configured = context.Configuration.GetSection(TeacherServerOptions.SectionName).Get<TeacherServerOptions>() ?? new TeacherServerOptions();
            kestrel.ListenAnyIP(configured.HttpPort);
        });

        builder.Host.UseSerilog((context, _, loggerConfiguration) =>
        {
            var logsDirectory = AppPaths.GetLogsPath();
            loggerConfiguration
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
                .WriteTo.File(Path.Combine(logsDirectory, "teacher-server-.log"), rollingInterval: RollingInterval.Day, formatProvider: CultureInfo.InvariantCulture);
        });
    }

    /// <summary>
    /// Configures HTTP pipeline.
    /// </summary>
    public static void ConfigureApp(WebApplication app)
    {
        app.UseRouting();
        app.UseCors(CorsPolicyName);
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapControllers();
        app.MapPost("/api/window/show", (HttpContext context, IHostControlService hostControlService) =>
        {
            if (context.Connection.RemoteIpAddress is not null && !IPAddress.IsLoopback(context.Connection.RemoteIpAddress))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            hostControlService.RequestShow();
            return Results.Ok(new { ok = true, message = "Show requested." });
        });
        app.MapHub<StudentHub>(HubRoutes.StudentHub);
        app.MapHub<TeacherHub>(HubRoutes.TeacherHub).RequireAuthorization(TeacherAuthDefaults.TeacherPolicy);
        app.MapFallbackToFile("index.html");

        using var scope = app.Services.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<IStorageInitializer>();
        initializer.EnsureCreatedAsync().GetAwaiter().GetResult();

        var tokenProvider = scope.ServiceProvider.GetRequiredService<ITeacherApiTokenProvider>();
        _ = tokenProvider.GetTokenAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private static string ResolvePath(string configuredPath, string fallbackFileName)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath) ? fallbackFileName : configuredPath;
        return Path.IsPathRooted(path) ? path : Path.Combine(AppPaths.GetBasePath(), path);
    }

    private static bool IsValidTeacherApiToken(string? configuredToken) =>
        string.IsNullOrWhiteSpace(configuredToken) || configuredToken.Trim().Length >= 16;

    private static bool IsValidCorsOrigins(string[]? origins)
    {
        if (origins is null || origins.Length == 0)
        {
            return true;
        }

        foreach (var origin in origins)
        {
            if (string.IsNullOrWhiteSpace(origin))
            {
                continue;
            }

            if (!Uri.TryCreate(origin.Trim(), UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static HashSet<string> BuildConfiguredCorsOriginSet(IEnumerable<string>? configuredOrigins)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (configuredOrigins is null)
        {
            return set;
        }

        foreach (var raw in configuredOrigins)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri))
            {
                continue;
            }

            if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            set.Add(uri.GetLeftPart(UriPartial.Authority));
        }

        return set;
    }

    private static bool IsAllowedCorsOrigin(string origin, IReadOnlySet<string> configuredOrigins)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var authority = uri.GetLeftPart(UriPartial.Authority);
        if (configuredOrigins.Contains(authority))
        {
            return true;
        }

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address))
        {
            return true;
        }

        return false;
    }
}
