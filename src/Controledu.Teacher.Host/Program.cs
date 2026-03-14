using Controledu.Teacher.Host.Options;
using Controledu.Teacher.Server;
using Controledu.Teacher.Server.Options;
using Controledu.Teacher.Server.Services;
using Controledu.Host.Core;
using Controledu.Common.Updates;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Runtime.InteropServices;

namespace Controledu.Teacher.Host;

internal static class Program
{
    private const string AppUserModelId = "Controledu";

    [STAThread]
    private static void Main(string[] args)
    {
        _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        ApplicationConfiguration.Initialize();

        WebApplication? serverApp = null;
        try
        {
            serverApp = TeacherServerHostFactory.Build(args, builder =>
            {
                builder.Configuration
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("hostsettings.json", optional: true, reloadOnChange: true)
                    .AddJsonFile($"hostsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables()
                    .AddCommandLine(args);

                builder.Services
                    .AddOptions<TeacherHostOptions>()
                    .Bind(builder.Configuration.GetSection(TeacherHostOptions.SectionName))
                    .Validate(static options => !string.IsNullOrWhiteSpace(options.WindowTitle), "TeacherHost:WindowTitle is required.")
                    .Validate(static options => string.IsNullOrWhiteSpace(options.UiUrl) || IsValidHttpHttpsUrl(options.UiUrl), "TeacherHost:UiUrl must be an absolute HTTP/HTTPS URL when specified.")
                    .ValidateOnStart();

                builder.Services
                    .AddOptions<AutoUpdateOptions>()
                    .Bind(builder.Configuration.GetSection(AutoUpdateOptions.SectionName))
                    .Validate(static options => options.StartupDelaySeconds is >= 0 and <= 3600, "AutoUpdate:StartupDelaySeconds must be in range 0..3600.")
                    .Validate(static options => options.CheckIntervalMinutes is >= 1 and <= 24 * 60, "AutoUpdate:CheckIntervalMinutes must be in range 1..1440.")
                    .Validate(static options => options.DownloadTimeoutSeconds is >= 5 and <= 3600, "AutoUpdate:DownloadTimeoutSeconds must be in range 5..3600.")
                    .Validate(static options => string.IsNullOrWhiteSpace(options.ManifestUrl) || IsValidHttpHttpsUrl(options.ManifestUrl), "AutoUpdate:ManifestUrl must be an absolute HTTP/HTTPS URL when specified.")
                    .ValidateOnStart();
            });

            serverApp.StartAsync().GetAwaiter().GetResult();

            var hostOptions = serverApp.Services.GetRequiredService<IOptions<TeacherHostOptions>>().Value;
            var serverOptions = serverApp.Services.GetRequiredService<IOptions<TeacherServerOptions>>().Value;
            var autoUpdateOptions = serverApp.Services.GetRequiredService<IOptions<AutoUpdateOptions>>().Value;
            var effectiveAutoUpdateOptions = new AutoUpdateOptions
            {
                Enabled = autoUpdateOptions.Enabled,
                ManifestUrl = string.IsNullOrWhiteSpace(autoUpdateOptions.ManifestUrl)
                    ? "https://controledu.kilocraft.org/updates/teacher/manifest.json"
                    : autoUpdateOptions.ManifestUrl,
                StartupDelaySeconds = autoUpdateOptions.StartupDelaySeconds,
                CheckIntervalMinutes = autoUpdateOptions.CheckIntervalMinutes,
                DownloadTimeoutSeconds = autoUpdateOptions.DownloadTimeoutSeconds,
            };

            var uiUrl = string.IsNullOrWhiteSpace(hostOptions.UiUrl)
                ? $"http://127.0.0.1:{serverOptions.HttpPort}/"
                : hostOptions.UiUrl;

            var desktopNotificationService = serverApp.Services.GetRequiredService<IDesktopNotificationService>();
            var hostControlService = serverApp.Services.GetRequiredService<IHostControlService>();
            using var form = new Form1(uiUrl!, hostOptions, effectiveAutoUpdateOptions, desktopNotificationService, hostControlService);
            Application.Run(form);
        }
        catch (Exception ex)
        {
            if (TryActivateExistingInstance(args))
            {
                return;
            }

            MessageBox.Show(
                $"Console host failed to start.\n\n{ex}",
                "Controledu Console",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            if (serverApp is not null)
            {
                try
                {
                    serverApp.StopAsync().GetAwaiter().GetResult();
                }
                catch
                {
                    // Ignore shutdown exceptions on app exit.
                }

                serverApp.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

    private static bool IsValidHttpHttpsUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryActivateExistingInstance(string[] args)
    {
        try
        {
            var options = LoadTeacherServerOptions(args);
            return SingleInstanceActivationClient.TryActivateWindow(options.HttpPort);
        }
        catch
        {
            return false;
        }
    }

    private static TeacherServerOptions LoadTeacherServerOptions(string[] args)
    {
        var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("hostsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"hostsettings.{environmentName}.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        return configuration.GetSection(TeacherServerOptions.SectionName).Get<TeacherServerOptions>() ?? new TeacherServerOptions();
    }
}
