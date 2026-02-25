using Controledu.Common.Runtime;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Controledu.Updater;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var options = ParseArgs(args);
            var logger = new FileLogger(options.LogPath);

            logger.Write($"Starting updater for product={options.Product}, installer={options.InstallerPath}");

            if (!File.Exists(options.InstallerPath))
            {
                logger.Write("Installer file does not exist.");
                return 2;
            }

            WaitForProcessExit(options.WaitPid, logger);

            var installerExitCode = RunInstaller(options, logger);
            if (installerExitCode != 0)
            {
                logger.Write($"Installer failed with exit code {installerExitCode}.");
                return installerExitCode;
            }

            if (!string.IsNullOrWhiteSpace(options.RestartPath) && File.Exists(options.RestartPath))
            {
                try
                {
                    var restartInfo = new ProcessStartInfo(options.RestartPath)
                    {
                        UseShellExecute = true,
                        Arguments = options.RestartArguments ?? string.Empty,
                        WorkingDirectory = Path.GetDirectoryName(options.RestartPath) ?? AppContext.BaseDirectory,
                    };
                    _ = Process.Start(restartInfo);
                    logger.Write($"Restarted application: {options.RestartPath}");
                }
                catch (Exception ex)
                {
                    logger.Write($"Failed to restart application: {ex}");
                    return 3;
                }
            }

            logger.Write("Updater finished successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                var fallback = new FileLogger(null);
                fallback.Write($"Fatal updater error: {ex}");
            }
            catch
            {
                // Ignore fallback logging failures.
            }

            return 1;
        }
    }

    private static UpdateLaunchOptions ParseArgs(IReadOnlyList<string> args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Count; i++)
        {
            var key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var value = i + 1 < args.Count ? args[i + 1] : string.Empty;
            if (!value.StartsWith("--", StringComparison.Ordinal))
            {
                map[key] = value;
                i++;
            }
            else
            {
                map[key] = "true";
            }
        }

        if (!map.TryGetValue("--installer", out var installerPath) || string.IsNullOrWhiteSpace(installerPath))
        {
            throw new InvalidOperationException("Missing --installer argument.");
        }

        map.TryGetValue("--product", out var product);
        map.TryGetValue("--restart", out var restartPath);
        map.TryGetValue("--restart-args", out var restartArgs);
        map.TryGetValue("--log", out var logPath);
        var waitPid = 0;
        if (map.TryGetValue("--wait-pid", out var waitPidRaw))
        {
            _ = int.TryParse(waitPidRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out waitPid);
        }

        return new UpdateLaunchOptions(
            InstallerPath: installerPath,
            Product: string.IsNullOrWhiteSpace(product) ? "unknown" : product,
            WaitPid: waitPid,
            RestartPath: restartPath,
            RestartArguments: restartArgs,
            LogPath: logPath);
    }

    private static void WaitForProcessExit(int pid, FileLogger logger)
    {
        if (pid <= 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            logger.Write($"Waiting for process {pid} to exit.");
            if (!process.WaitForExit(120_000))
            {
                logger.Write($"Process {pid} did not exit within timeout.");
            }
        }
        catch (ArgumentException)
        {
            // Already exited.
        }
        catch (Exception ex)
        {
            logger.Write($"Failed while waiting for process {pid}: {ex}");
        }
    }

    private static int RunInstaller(UpdateLaunchOptions options, FileLogger logger)
    {
        var args = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS";
        var startInfo = new ProcessStartInfo(options.InstallerPath, args)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(options.InstallerPath) ?? AppContext.BaseDirectory,
        };

        logger.Write($"Launching installer: {options.InstallerPath} {args}");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start installer process.");
        process.WaitForExit();
        logger.Write($"Installer exit code: {process.ExitCode}");
        return process.ExitCode;
    }

    private sealed record UpdateLaunchOptions(
        string InstallerPath,
        string Product,
        int WaitPid,
        string? RestartPath,
        string? RestartArguments,
        string? LogPath);

    private sealed class FileLogger
    {
        private readonly string _path;
        private readonly object _sync = new();

        public FileLogger(string? path)
        {
            _path = string.IsNullOrWhiteSpace(path)
                ? Path.Combine(AppPaths.GetLogsPath(), "updater.log")
                : path;

            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        public void Write(string message)
        {
            var line = $"{DateTimeOffset.UtcNow:O} {message}";
            lock (_sync)
            {
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
    }
}
