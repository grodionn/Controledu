using Controledu.Common.Runtime;
using Controledu.Student.Host.Contracts;
using Controledu.Storage.Stores;
using Controledu.Transport.Dto;
using System.IO.Compression;
using System.Text.Json;

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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DetectionStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var effectivePolicyRaw = await settingsStore.GetAsync(DetectionSettingKeys.EffectivePolicyJson, cancellationToken);
        var localPolicyRaw = await settingsStore.GetAsync(DetectionSettingKeys.LocalPolicyJson, cancellationToken);
        var lastCheck = await settingsStore.GetAsync(DetectionSettingKeys.LastCheckUtc, cancellationToken);
        var lastResult = await settingsStore.GetAsync(DetectionSettingKeys.LastResult, cancellationToken);
        var modelVersion = await settingsStore.GetAsync(DetectionSettingKeys.LastModelVersion, cancellationToken);

        var effectivePolicy = DeserializePolicy(effectivePolicyRaw) ?? new DetectionPolicyDto();
        var localPolicy = DeserializePolicy(localPolicyRaw);
        var resolvedPolicy = localPolicy is null
            ? effectivePolicy
            : effectivePolicy with
            {
                Enabled = localPolicy.Enabled,
            };

        var productionPolicy = DetectionPolicyFactory.CreateProductionPolicy(resolvedPolicy.Enabled);
        var dataCollectionEnabled = false;

        return new DetectionStatusResponse(
            DetectionEnabled: productionPolicy.Enabled,
            DataCollectionModeEnabled: dataCollectionEnabled,
            LastCheckUtc: lastCheck,
            LastResult: lastResult,
            LastModelVersion: modelVersion,
            MetadataThreshold: productionPolicy.MetadataThreshold,
            MlThreshold: productionPolicy.MlThreshold,
            SampleRate: 0,
            LocalRetentionDays: 1);
    }

    public async Task UpdateLocalConfigAsync(DetectionConfigUpdateRequest request, CancellationToken cancellationToken = default)
    {
        _ = request;
        var localPolicy = DetectionPolicyFactory.CreateProductionPolicy(enabled: true);

        await settingsStore.SetAsync(DetectionSettingKeys.LocalPolicyJson, JsonSerializer.Serialize(localPolicy, JsonOptions), cancellationToken);
    }

    public Task TriggerSelfTestAsync(CancellationToken cancellationToken = default) =>
        settingsStore.SetAsync(DetectionSettingKeys.SelfTestRequest, "1", cancellationToken);

    public Task<string> ExportDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var exportName = $"student-diagnostics-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip";
        var exportPath = Path.Combine(AppPaths.GetExportsPath(), exportName);

        if (File.Exists(exportPath))
        {
            File.Delete(exportPath);
        }

        using (var archive = ZipFile.Open(exportPath, ZipArchiveMode.Create))
        {
            AddDirectoryIfExists(archive, AppPaths.GetLogsPath(), "logs");
            AddDirectoryIfExists(archive, Path.Combine(AppPaths.GetDatasetsPath(), "dataset"), "dataset");

            var dbPath = Path.Combine(AppPaths.GetBasePath(), "student-shared.db");
            if (File.Exists(dbPath))
            {
                archive.CreateEntryFromFile(dbPath, "student-shared.db", CompressionLevel.Fastest);
            }
        }

        return Task.FromResult(exportPath);
    }

    private static void AddDirectoryIfExists(ZipArchive archive, string sourceDir, string entryPrefix)
    {
        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var normalized = relative.Replace('\\', '/');
            archive.CreateEntryFromFile(file, $"{entryPrefix}/{normalized}", CompressionLevel.Fastest);
        }
    }

    private static DetectionPolicyDto? DeserializePolicy(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DetectionPolicyDto>(raw, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
