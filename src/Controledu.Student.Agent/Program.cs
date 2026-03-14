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

var studentAgentSection = builder.Configuration.GetSection(StudentAgentOptions.SectionName);
builder.Services
    .AddOptions<StudentAgentOptions>()
    .Bind(studentAgentSection)
    .Validate(static options => !string.IsNullOrWhiteSpace(options.StorageFile), "StudentAgent:StorageFile is required.")
    .Validate(static options => options.LocalHostPort is >= 1 and <= 65535, "StudentAgent:LocalHostPort must be in range 1..65535.")
    .Validate(static options => options.HeartbeatIntervalSeconds is >= 1 and <= 300, "StudentAgent:HeartbeatIntervalSeconds must be in range 1..300.")
    .Validate(static options => options.InitialFps is >= 1 and <= 120, "StudentAgent:InitialFps must be in range 1..120.")
    .Validate(static options => options.MinFps is >= 1 and <= 120, "StudentAgent:MinFps must be in range 1..120.")
    .Validate(static options => options.MaxFps is >= 1 and <= 240, "StudentAgent:MaxFps must be in range 1..240.")
    .Validate(static options => options.MinFps <= options.InitialFps && options.InitialFps <= options.MaxFps, "StudentAgent FPS bounds must satisfy MinFps <= InitialFps <= MaxFps.")
    .Validate(static options => options.InitialJpegQuality is >= 1 and <= 100, "StudentAgent:InitialJpegQuality must be in range 1..100.")
    .Validate(static options => options.MinJpegQuality is >= 1 and <= 100, "StudentAgent:MinJpegQuality must be in range 1..100.")
    .Validate(static options => options.MaxJpegQuality is >= 1 and <= 100, "StudentAgent:MaxJpegQuality must be in range 1..100.")
    .Validate(static options => options.MinJpegQuality <= options.InitialJpegQuality && options.InitialJpegQuality <= options.MaxJpegQuality, "StudentAgent JPEG bounds must satisfy MinJpegQuality <= InitialJpegQuality <= MaxJpegQuality.")
    .Validate(static options => options.MaxCaptureWidth is >= 320 and <= 7680, "StudentAgent:MaxCaptureWidth must be in range 320..7680.")
    .Validate(static options => options.MaxCaptureHeight is >= 240 and <= 4320, "StudentAgent:MaxCaptureHeight must be in range 240..4320.")
    .Validate(static options => options.HandRaiseCooldownSeconds is >= 1 and <= 300, "StudentAgent:HandRaiseCooldownSeconds must be in range 1..300.")
    .Validate(static options => options.ChunkSize is >= 16 * 1024 and <= 4 * 1024 * 1024, "StudentAgent:ChunkSize must be in range 16384..4194304.")
    .Validate(static options => options.Detection is not null, "StudentAgent:Detection section is required.")
    .Validate(static options => IsValidTeacherTtsOptions(options.TeacherTts), "StudentAgent:TeacherTts section contains invalid values.")
    .ValidateOnStart();

builder.Services
    .AddOptions<OnnxModelConfig>()
    .Bind(studentAgentSection.GetSection("Onnx"))
    .Validate(static config => config.EnableBinary || config.EnableMulticlass, "StudentAgent:Onnx must enable at least one model.")
    .Validate(static config => !config.EnableBinary || !string.IsNullOrWhiteSpace(config.BinaryModelPath), "StudentAgent:Onnx:BinaryModelPath is required when binary model is enabled.")
    .Validate(static config => !config.EnableMulticlass || !string.IsNullOrWhiteSpace(config.MulticlassModelPath), "StudentAgent:Onnx:MulticlassModelPath is required when multiclass model is enabled.")
    .Validate(static config => config.InputWidth is >= 32 and <= 4096, "StudentAgent:Onnx:InputWidth must be in range 32..4096.")
    .Validate(static config => config.InputHeight is >= 32 and <= 4096, "StudentAgent:Onnx:InputHeight must be in range 32..4096.")
    .ValidateOnStart();

var agentOptions = studentAgentSection.Get<StudentAgentOptions>() ?? new StudentAgentOptions();
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
builder.Services.AddSingleton<ICaptureCycleService, CaptureCycleService>();
builder.Services.AddSingleton<IDetectionCycleService, DetectionCycleService>();
builder.Services.AddSingleton<ISyncChatCycleService, SyncChatCycleService>();
builder.Services.AddSingleton<IFileTransferCycleService, FileTransferCycleService>();
builder.Services.AddSingleton<IRemoteControlCycleService, RemoteControlCycleService>();
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

static bool IsValidTeacherTtsOptions(StudentTeacherTtsOptions? options)
{
    if (options is null)
    {
        return false;
    }

    if (options.MaxTextLength is < 1 or > 4000)
    {
        return false;
    }

    if (options.SpeakingRate is < 0.25 or > 4.0)
    {
        return false;
    }

    if (options.Pitch is < -20.0 or > 20.0)
    {
        return false;
    }

    if (options.SelfHostTimeoutSeconds is < 5 or > 120)
    {
        return false;
    }

    if (string.IsNullOrWhiteSpace(options.SelfHostTtsPath) || !options.SelfHostTtsPath.StartsWith('/'))
    {
        return false;
    }

    if (!IsNullOrHttpHttpsUrl(options.SelfHostBaseUrl))
    {
        return false;
    }

    var provider = (options.Provider ?? string.Empty).Trim();
    return provider.Length > 0
        && (provider.Equals("google", StringComparison.OrdinalIgnoreCase)
            || provider.Equals("selfhost", StringComparison.OrdinalIgnoreCase)
            || provider.Equals("disabled", StringComparison.OrdinalIgnoreCase));
}

static bool IsNullOrHttpHttpsUrl(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return true;
    }

    if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
    {
        return false;
    }

    return string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);
}

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
