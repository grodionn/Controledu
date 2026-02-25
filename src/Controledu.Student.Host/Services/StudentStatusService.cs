using Controledu.Student.Host.Contracts;
using Controledu.Common.Runtime;
using Controledu.Storage.Stores;

namespace Controledu.Student.Host.Services;

/// <summary>
/// Builds status payload for student UI.
/// </summary>
public interface IStudentStatusService
{
    /// <summary>
    /// Returns aggregate status snapshot.
    /// </summary>
    Task<StudentStatusResponse> GetAsync(CancellationToken cancellationToken = default);
}

internal sealed class StudentStatusService(
    IAdminPasswordService adminPasswordService,
    IStudentPairingService pairingService,
    IDeviceIdentityService deviceIdentityService,
    IAgentAutoStartManager agentAutoStartManager,
    IAgentProcessManager agentProcessManager,
    ISettingsStore settingsStore) : IStudentStatusService
{
    public async Task<StudentStatusResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var hasAdminPassword = await adminPasswordService.HasPasswordAsync(cancellationToken);
        var binding = await pairingService.GetBindingAsync(cancellationToken);
        var deviceName = await deviceIdentityService.GetDisplayNameAsync(cancellationToken);
        var isPaired = binding is not null;
        var serverOnline = binding is not null && await pairingService.CheckServerOnlineAsync(binding, cancellationToken);
        var agentAutoStart = await agentAutoStartManager.GetEnabledAsync(cancellationToken);
        var agentRunning = agentProcessManager.IsRunning;
        var lastAlert = await settingsStore.GetAsync(DetectionSettingKeys.LastAlert, cancellationToken);
        var lastCheckUtc = await settingsStore.GetAsync(DetectionSettingKeys.LastCheckUtc, cancellationToken);
        var detectionEnabledRaw = await settingsStore.GetAsync(DetectionSettingKeys.EffectivePolicyJson, cancellationToken);
        var dataCollectionRaw = await settingsStore.GetAsync(DetectionSettingKeys.DataCollectionEnabled, cancellationToken);

        var detectionEnabled = true;
        if (!string.IsNullOrWhiteSpace(detectionEnabledRaw))
        {
            detectionEnabled = !detectionEnabledRaw.Contains("\"enabled\":false", StringComparison.OrdinalIgnoreCase);
        }

        var dataCollectionEnabled = string.Equals(dataCollectionRaw, "1", StringComparison.Ordinal);

        return new StudentStatusResponse(
            hasAdminPassword,
            isPaired,
            deviceName,
            binding?.ServerName,
            binding?.ServerBaseUrl,
            serverOnline,
            isPaired && serverOnline,
            agentAutoStart,
            agentRunning,
            lastAlert,
            detectionEnabled,
            dataCollectionEnabled,
            lastCheckUtc);
    }
}
