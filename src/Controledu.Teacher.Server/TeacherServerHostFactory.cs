using Controledu.Common.Runtime;
using Controledu.Transport.Constants;
using Controledu.Storage.Extensions;
using Controledu.Storage.Stores;
using Controledu.Teacher.Server.Hubs;
using Controledu.Teacher.Server.Options;
using Controledu.Teacher.Server.Services;
using Serilog;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Controledu.Teacher.Server;

/// <summary>
/// Factory for standalone and embedded teacher server host.
/// </summary>
public static class TeacherServerHostFactory
{
    /// <summary>
    /// Builds configured web application instance.
    /// </summary>
    public static WebApplication Build(string[]? args = null, Action<WebApplicationBuilder>? configureBuilder = null)
    {
        var builder = WebApplication.CreateBuilder(args ?? []);
        ConfigureBuilder(builder);
        configureBuilder?.Invoke(builder);

        var app = builder.Build();
        ConfigureApp(app);
        return app;
    }

    /// <summary>
    /// Configures dependency injection and host settings.
    /// </summary>
    public static void ConfigureBuilder(WebApplicationBuilder builder)
    {
        builder.Services.Configure<TeacherServerOptions>(builder.Configuration.GetSection(TeacherServerOptions.SectionName));

        var options = builder.Configuration.GetSection(TeacherServerOptions.SectionName).Get<TeacherServerOptions>() ?? new TeacherServerOptions();
        var sqlitePath = ResolvePath(options.StorageFile, "teacher-server.db");

        builder.Services.AddControleduStorage(sqlitePath);
        builder.Services
            .AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            })
            .AddApplicationPart(typeof(TeacherServerAssemblyMarker).Assembly);
        builder.Services.AddSignalR(hubOptions =>
        {
            hubOptions.MaximumReceiveMessageSize = Math.Max(32L * 1024L, options.SignalRMaxReceiveMessageBytes);
        })
        .AddJsonProtocol(options =>
        {
            options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
        builder.Services.AddCors(policy =>
        {
            policy.AddDefaultPolicy(cors => cors
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
                .SetIsOriginAllowed(_ => true));
        });

        builder.Services.AddSingleton<ISystemClock, SystemClock>();
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
        app.UseCors();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapControllers();
        app.MapHub<StudentHub>(HubRoutes.StudentHub);
        app.MapHub<TeacherHub>(HubRoutes.TeacherHub);
        app.MapFallbackToFile("index.html");

        using var scope = app.Services.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<IStorageInitializer>();
        initializer.EnsureCreatedAsync().GetAwaiter().GetResult();
    }

    private static string ResolvePath(string configuredPath, string fallbackFileName)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath) ? fallbackFileName : configuredPath;
        return Path.IsPathRooted(path) ? path : Path.Combine(AppPaths.GetBasePath(), path);
    }
}

