using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Windows.Forms;

namespace Controledu.Student.Host.Services;

/// <summary>
/// Applies Student.Host autostart policy for the current Windows user.
/// </summary>
public interface IHostAutoStartManager
{
    /// <summary>
    /// Gets whether host autostart is currently enabled.
    /// </summary>
    Task<bool> GetEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables host autostart for the current user.
    /// </summary>
    Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}

internal sealed class HostAutoStartManager(ILogger<HostAutoStartManager> logger) : IHostAutoStartManager
{
    private const string RunRegistryPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string RunRegistryName = "ControleduStudentHost";

    public Task<bool> GetEnabledAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(false);
        }

        try
        {
            var expectedValue = BuildRegistryValue();
            return Task.FromResult(
                IsRunValueConfigured(Registry.CurrentUser, expectedValue)
                || IsRunValueConfigured(Registry.LocalMachine, expectedValue));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read Windows autostart policy for Student.Host");
            return Task.FromResult(false);
        }
    }

    public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Task.CompletedTask;
        }

        try
        {
            var executablePath = ResolveHostExecutablePath();
            var expectedValue = BuildRegistryValue(executablePath);

            if (IsRunValueConfigured(Registry.LocalMachine, expectedValue))
            {
                if (enabled)
                {
                    return Task.CompletedTask;
                }

                DeleteCurrentUserRunValue();
                throw new InvalidOperationException("Student.Host autostart is enabled machine-wide by the installer and requires administrator rights to disable.");
            }

            using var key = Registry.CurrentUser.CreateSubKey(RunRegistryPath, writable: true)
                ?? throw new InvalidOperationException("Windows autostart registry key is unavailable.");

            if (!enabled)
            {
                key.DeleteValue(RunRegistryName, throwOnMissingValue: false);
                return Task.CompletedTask;
            }

            if (!File.Exists(executablePath))
            {
                throw new InvalidOperationException($"Student host executable was not found: {executablePath}");
            }

            key.SetValue(RunRegistryName, expectedValue, RegistryValueKind.String);
            return Task.CompletedTask;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update Windows autostart policy for Student.Host");
            throw new InvalidOperationException("Failed to update Windows application autostart.", ex);
        }
    }

    private static string BuildRegistryValue() => BuildRegistryValue(ResolveHostExecutablePath());

    private static string BuildRegistryValue(string executablePath) => $"\"{executablePath}\"";

    private static string ResolveHostExecutablePath()
    {
        var executablePath = Application.ExecutablePath;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            return executablePath;
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            return processPath;
        }

        throw new InvalidOperationException("Student host executable path could not be resolved.");
    }

    private static bool IsRunValueConfigured(RegistryKey root, string expectedValue)
    {
        try
        {
            using var key = root.OpenSubKey(RunRegistryPath, writable: false);
            var configuredValue = key?.GetValue(RunRegistryName) as string;
            return string.Equals(configuredValue, expectedValue, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void DeleteCurrentUserRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: true);
        key?.DeleteValue(RunRegistryName, throwOnMissingValue: false);
    }
}
