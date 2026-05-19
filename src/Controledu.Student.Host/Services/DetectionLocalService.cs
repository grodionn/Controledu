using Controledu.Common.Runtime;
using Controledu.Student.Host.Contracts;
using Controledu.Storage.Stores;
using Controledu.Transport.Dto;

namespace Controledu.Student.Host.Services;

/// <summary>
/// Local detection settings and diagnostics service.
/// </summary>
public interface IDetectionLocalService
{
    /// <summary>
    /// Returns local detection status snapshot.
    /// </summary>
    Task<DetectionStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates local detection override policy.
    /// </summary>
    Task UpdateLocalConfigAsync(DetectionConfigUpdateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers one-shot detector self-test alert in agent.
    /// </summary>
    Task TriggerSelfTestAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports diagnostics ZIP and returns file path.
    /// </summary>
    Task<string> ExportDiagnosticsAsync(CancellationToken cancellationToken = default);
}

internal sealed class DetectionLocalService(ISettingsStore settingsStore) : IDetectionLocalService
{
    public async Task<DetectionStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var lastCheck = await settingsStore.GetAsync(DetectionSettingKeys.LastCheckUtc, cancellationToken);
        var lastResult = await settingsStore.GetAsync(DetectionSettingKeys.LastResult, cancellationToken);
        var modelVersion = await settingsStore.GetAsync(DetectionSettingKeys.LastModelVersion, cancellationToken);

        var productionPolicy = DetectionPolicyFactory.CreateProductionPolicy(enabled: true);

        return new DetectionStatusResponse(
            DetectionEnabled: productionPolicy.Enabled,
            DataCollectionModeEnabled: false,
            LastCheckUtc: lastCheck,
            LastResult: lastResult,
            LastModelVersion: modelVersion,
            MetadataThreshold: productionPolicy.MetadataThreshold,
            MlThreshold: productionPolicy.MlThreshold,
            SampleRate: 0,
            LocalRetentionDays: 1);
    }

    public Task UpdateLocalConfigAsync(DetectionConfigUpdateRequest request, CancellationToken cancellationToken = default)
    {
        _ = request;
        _ = cancellationToken;
        throw new InvalidOperationException("Detection configuration is locked in production mode.");
    }

    public Task TriggerSelfTestAsync(CancellationToken cancellationToken = default) =>
        settingsStore.SetAsync(DetectionSettingKeys.SelfTestRequest, "1", cancellationToken);

    public Task<string> ExportDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var archivePath = DiagnosticsArchiveBuilder.CreateStudentDiagnosticsArchive(
            new Dictionary<string, string?>
            {
                ["source"] = "student-host-local-export",
            },
            cancellationToken);

        return Task.FromResult(archivePath);
    }
}
