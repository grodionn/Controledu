using Controledu.Common.Runtime;
using Controledu.Common.Security;
using Controledu.Detection.Abstractions;
using Controledu.Detection.Core;
using Controledu.Detection.Onnx;
using Controledu.Storage.Extensions;
using Controledu.Student.Agent;
using Controledu.Student.Agent.Options;
using Controledu.Student.Agent.Services;
using Serilog;
using System.Globalization;
using System.Runtime.InteropServices;

TryEnableDpiAwareness();

var builder = Host.CreateApplicationBuilder(args);

if (args.Any(static arg => string.Equals(arg, "--service", StringComparison.OrdinalIgnoreCase)))
{
    builder.Services.AddWindowsService();
}

builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

builder.Services.Configure<StudentAgentOptions>(builder.Configuration.GetSection(StudentAgentOptions.SectionName));
builder.Services.Configure<OnnxModelConfig>(builder.Configuration.GetSection($"{StudentAgentOptions.SectionName}:Onnx"));

var agentOptions = builder.Configuration.GetSection(StudentAgentOptions.SectionName).Get<StudentAgentOptions>() ?? new StudentAgentOptions();
var storagePath = Path.IsPathRooted(agentOptions.StorageFile)
    ? agentOptions.StorageFile
    : Path.Combine(AppPaths.GetBasePath(), agentOptions.StorageFile);

builder.Services.AddControleduStorage(storagePath);
builder.Services.AddSingleton<ISecretProtector>(_ => SecretProtectorFactory.CreateDefault());

builder.Services.AddSingleton<IBindingResolver, BindingResolver>();
builder.Services.AddSingleton<IActiveWindowProvider, ActiveWindowProvider>();
builder.Services.AddSingleton<IScreenCaptureService, ScreenCaptureService>();
builder.Services.AddSingleton<StudentHubClient>();
builder.Services.AddSingleton<FileTransferReceiver>();
builder.Services.AddSingleton<DatasetCollectionService>();
builder.Services.AddSingleton<DiagnosticsExportUploader>();
builder.Services.AddSingleton<IStudentLocalHostClient, StudentLocalHostClient>();
builder.Services.AddSingleton<ITeacherTtsSynthesisService, TeacherTtsSynthesisService>();
builder.Services.AddSingleton<TeacherTtsPlaybackService>();
builder.Services.AddSingleton<ITeacherTtsPlaybackQueue>(serviceProvider => serviceProvider.GetRequiredService<TeacherTtsPlaybackService>());
builder.Services.AddSingleton<IRemoteControlInputExecutor, WindowsRemoteControlInputExecutor>();
builder.Services.AddSingleton<IRemoteControlService, RemoteControlService>();
builder.Services.AddHttpClient();

builder.Services.AddSingleton<IFrameChangeFilter, PerceptualHashChangeFilter>();
builder.Services.AddSingleton<WindowMetadataDetector>();
builder.Services.AddSingleton<ITemporalSmoother, TemporalVotingSmoother>();
builder.Services.AddSingleton<IAiUiDetector, OnnxBinaryAiDetector>();
builder.Services.AddSingleton<IAiUiDetector, OnnxMulticlassAiDetector>();
builder.Services.AddSingleton<DetectionPipeline>();

builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<TeacherTtsPlaybackService>());

builder.Services.AddSerilog((serviceProvider, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
        .WriteTo.File(Path.Combine(AppPaths.GetLogsPath(), "student-agent-.log"), rollingInterval: RollingInterval.Day, formatProvider: CultureInfo.InvariantCulture);
});

var host = builder.Build();
await host.RunAsync();

static void TryEnableDpiAwareness()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    try
    {
        // Prefer per-monitor V2 when available (Windows 10+). Fallback to system DPI aware.
        if (SetProcessDpiAwarenessContext(new IntPtr(-4)))
        {
            return;
        }
    }
    catch
    {
        // Fallback below.
    }

    try
    {
        _ = SetProcessDPIAware();
    }
    catch
    {
        // Ignore: capture/input still work, but may be DPI-virtualized on scaled displays.
    }
}

[DllImport("user32.dll", SetLastError = true)]
static extern bool SetProcessDPIAware();

[DllImport("user32.dll", SetLastError = true)]
static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
