using Controledu.Student.Host.Options;
using Controledu.Student.Host.Services;
using Controledu.Host.Core;
using Controledu.Common.Updates;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Controledu.Student.Host;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        WebApplication? localApp = null;
        try
        {
            localApp = StudentLocalHostFactory.Build(args);
            localApp.StartAsync().GetAwaiter().GetResult();

            var options = localApp.Services.GetRequiredService<IOptions<StudentHostOptions>>().Value;
            var autoUpdateOptions = localApp.Services.GetRequiredService<IOptions<AutoUpdateOptions>>().Value;
            var effectiveAutoUpdateOptions = new AutoUpdateOptions
            {
                Enabled = autoUpdateOptions.Enabled,
                ManifestUrl = string.IsNullOrWhiteSpace(autoUpdateOptions.ManifestUrl)
                    ? "https://controledu.kilocraft.org/updates/student/manifest.json"
                    : autoUpdateOptions.ManifestUrl,
                StartupDelaySeconds = autoUpdateOptions.StartupDelaySeconds,
                CheckIntervalMinutes = autoUpdateOptions.CheckIntervalMinutes,
                DownloadTimeoutSeconds = autoUpdateOptions.DownloadTimeoutSeconds,
            };
            var hostControlService = localApp.Services.GetRequiredService<IHostControlService>();
            var handRaiseRequestService = localApp.Services.GetRequiredService<IHandRaiseRequestService>();
            var remoteControlConsentService = localApp.Services.GetRequiredService<IRemoteControlConsentService>();
            var uiUrl = $"http://127.0.0.1:{options.LocalPort}/";

            using var form = new Form1(uiUrl, options, effectiveAutoUpdateOptions, hostControlService, handRaiseRequestService, remoteControlConsentService);
            Application.Run(form);
        }
        catch (Exception ex)
        {
            if (TryActivateExistingInstance(args))
            {
                return;
            }

            MessageBox.Show(
                $"Endpoint host failed to start.\n\n{ex}",
                "Controledu Endpoint",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            if (localApp is not null)
            {
                try
                {
                    localApp.StopAsync().GetAwaiter().GetResult();
                }
                catch
                {
                    // Ignore shutdown exceptions on app exit.
                }

                localApp.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }

    private static bool TryActivateExistingInstance(string[] args)
    {
        try
        {
            var options = LoadStudentHostOptions(args);
            return SingleInstanceActivationClient.TryActivateWindow(options.LocalPort);
        }
        catch
        {
            return false;
        }
    }

    private static StudentHostOptions LoadStudentHostOptions(string[] args)
    {
        var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        return configuration.GetSection(StudentHostOptions.SectionName).Get<StudentHostOptions>() ?? new StudentHostOptions();
    }
}
