using Controledu.Common.Runtime;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Controledu.Host.Core;

/// <summary>
/// Shared auto-update helpers for desktop hosts.
/// </summary>
public static class HostAutoUpdateUtility
{
    /// <summary>
    /// Launches elevated updater process for downloaded installer.
    /// </summary>
    public static bool TryLaunchUpdater(
        string installerPath,
        string productKey,
        string restartExecutablePath,
        int waitProcessId,
        Action<string>? onTrace = null,
        Action<string>? onUserNotice = null)
    {
        try
        {
            var packagedUpdater = Path.Combine(AppContext.BaseDirectory, "Updater", "Controledu.Updater.exe");
            if (!File.Exists(packagedUpdater))
            {
                onTrace?.Invoke($"Updater missing: {packagedUpdater}");
                onUserNotice?.Invoke("Updater component is missing.");
                return false;
            }

            var tempUpdater = CopyUpdaterToTemp(packagedUpdater);
            var updaterLogPath = Path.Combine(AppPaths.GetLogsPath(), $"updater-{productKey}.log");
            var args = string.Join(" ", [
                "--installer", QuoteArg(installerPath),
                "--wait-pid", waitProcessId.ToString(CultureInfo.InvariantCulture),
                "--restart", QuoteArg(restartExecutablePath),
                "--product", QuoteArg(productKey),
                "--log", QuoteArg(updaterLogPath)
            ]);

            var startInfo = new ProcessStartInfo(tempUpdater, args)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(tempUpdater) ?? AppContext.BaseDirectory,
            };

            _ = Process.Start(startInfo);
            onTrace?.Invoke($"Started updater process from temp copy: {tempUpdater}");
            return true;
        }
        catch (Win32Exception ex)
        {
            onTrace?.Invoke($"UAC/launch error: {ex}");
            onUserNotice?.Invoke("Update requires administrator approval (UAC).");
            return false;
        }
        catch (Exception ex)
        {
            onTrace?.Invoke($"Updater launch failed: {ex}");
            onUserNotice?.Invoke($"Updater failed: {TrimNotification(ex.Message)}");
            return false;
        }
    }

    /// <summary>
    /// Appends one line to auto-update trace log.
    /// </summary>
    public static void WriteAutoUpdateTrace(string productKey, string message)
    {
        try
        {
            var logPath = Path.Combine(AppPaths.GetLogsPath(), $"auto-update-{productKey}.log");
            var line = $"[{DateTimeOffset.UtcNow:O}] {message}{Environment.NewLine}";
            File.AppendAllText(logPath, line, Encoding.UTF8);
        }
        catch
        {
            // Ignore logging failures.
        }
    }

    /// <summary>
    /// Trims long/empty error messages for end-user notifications.
    /// </summary>
    public static string TrimNotification(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Unknown error";
        }

        var trimmed = text.Trim().Replace(Environment.NewLine, " ");
        return trimmed.Length > 160 ? trimmed[..160] : trimmed;
    }

    private static string CopyUpdaterToTemp(string packagedUpdaterPath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Controledu", "UpdaterRuntime", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempUpdaterPath = Path.Combine(tempDir, Path.GetFileName(packagedUpdaterPath));
        File.Copy(packagedUpdaterPath, tempUpdaterPath, overwrite: true);
        return tempUpdaterPath;
    }

    private static string QuoteArg(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
