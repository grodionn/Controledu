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
            using var form = new Form1(uiUrl!, hostOptions, autoUpdateOptions, desktopNotificationService);
            Application.Run(form);
        }
        catch (Exception ex)
        {
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
}
