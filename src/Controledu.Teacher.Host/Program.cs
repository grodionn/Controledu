using Controledu.Teacher.Host.Options;
using Controledu.Teacher.Server;
using Controledu.Teacher.Server.Options;
using Controledu.Teacher.Server.Services;
using Controledu.Common.Updates;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            });

            serverApp.StartAsync().GetAwaiter().GetResult();

            var configuration = serverApp.Services.GetRequiredService<IConfiguration>();
            var hostOptions = configuration.GetSection(TeacherHostOptions.SectionName).Get<TeacherHostOptions>() ?? new TeacherHostOptions();
            var serverOptions = configuration.GetSection(TeacherServerOptions.SectionName).Get<TeacherServerOptions>() ?? new TeacherServerOptions();
            var autoUpdateOptions = configuration.GetSection(AutoUpdateOptions.SectionName).Get<AutoUpdateOptions>() ?? new AutoUpdateOptions();
            if (string.IsNullOrWhiteSpace(autoUpdateOptions.ManifestUrl))
            {
                autoUpdateOptions.ManifestUrl = "https://controledu.kilocraft.org/updates/teacher/manifest.json";
            }

            var uiUrl = string.IsNullOrWhiteSpace(hostOptions.UiUrl)
                ? $"http://127.0.0.1:{serverOptions.HttpPort}/"
                : hostOptions.UiUrl;

            var desktopNotificationService = serverApp.Services.GetRequiredService<IDesktopNotificationService>();
            var hostControlService = serverApp.Services.GetRequiredService<IHostControlService>();
            using var form = new Form1(uiUrl!, hostOptions, autoUpdateOptions, desktopNotificationService, hostControlService);
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

    private static bool TryActivateExistingInstance(string[] args)
    {
        try
        {
            var options = LoadTeacherServerOptions(args);
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(2),
            };

            var response = http.PostAsync($"http://127.0.0.1:{options.HttpPort}/api/window/show", content: null)
                .GetAwaiter()
                .GetResult();

            return response.IsSuccessStatusCode;
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
