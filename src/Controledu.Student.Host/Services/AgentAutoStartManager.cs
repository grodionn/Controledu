using Controledu.Student.Host.Options;
using Controledu.Storage.Stores;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Controledu.Student.Host.Services;

/// <summary>
/// Persists and applies Student.Agent autostart policy.
/// </summary>
public interface IAgentAutoStartManager
{
    /// <summary>
    /// Gets current autostart flag.
    /// </summary>
    Task<bool> GetEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores and applies autostart flag.
    /// </summary>
    Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}

internal sealed class AgentAutoStartManager(
    ISettingsStore settingsStore,
    IOptions<StudentHostOptions> options,
    ILogger<AgentAutoStartManager> logger) : IAgentAutoStartManager
{
    private const string AutoStartSettingKey = "student.agent.autostart";
    private const string RunRegistryPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string RunRegistryName = "ControleduStudentAgent";

    public async Task<bool> GetEnabledAsync(CancellationToken cancellationToken = default)
    {
        var value = await settingsStore.GetAsync(AutoStartSettingKey, cancellationToken);
        return bool.TryParse(value, out var enabled) && enabled;
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await settingsStore.SetAsync(AutoStartSettingKey, enabled.ToString(), cancellationToken);
        ApplyWindowsAutoStart(enabled);
    }

    private void ApplyWindowsAutoStart(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunRegistryPath, writable: true);
            if (key is null)
            {
                return;
            }

            if (!enabled)
            {
                key.DeleteValue(RunRegistryName, throwOnMissingValue: false);
                return;
            }

            var agentPath = ResolveAgentExecutablePath();
            if (!File.Exists(agentPath))
            {
                logger.LogWarning("Agent executable not found for autostart registration: {Path}", agentPath);
                return;
            }

            var value = $"\"{agentPath}\"";
            key.SetValue(RunRegistryName, value, RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to apply Windows autostart policy");
        }
    }

    private string ResolveAgentExecutablePath()
    {
        var configured = options.Value.AgentExecutablePath;
        if (Path.IsPathRooted(configured))
        {
            return configured;
        }

        return Path.Combine(AppContext.BaseDirectory, configured);
    }
}
