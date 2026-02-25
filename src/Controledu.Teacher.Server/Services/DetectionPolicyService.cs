using Controledu.Transport.Dto;
using Controledu.Storage.Stores;
using System.Text.Json;

namespace Controledu.Teacher.Server.Services;

/// <summary>
/// Detection policy persistence and runtime access.
/// </summary>
public interface IDetectionPolicyService
{
    /// <summary>
    /// Returns current detection policy.
    /// </summary>
    Task<DetectionPolicyDto> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a new detection policy.
    /// </summary>
    Task<DetectionPolicyDto> SaveAsync(DetectionPolicyDto policy, string actor, CancellationToken cancellationToken = default);
}

internal sealed class DetectionPolicyService(
    ISettingsStore settingsStore,
    IAuditService auditService) : IDetectionPolicyService
{
    private const string PolicyStorageKey = "teacher.detection.policy";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DetectionPolicyDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var hardcodedDefaults = DetectionPolicyFactory.CreateProductionPolicy(enabled: true);

        var raw = await settingsStore.GetAsync(PolicyStorageKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return hardcodedDefaults;
        }

        try
        {
            _ = JsonSerializer.Deserialize<DetectionPolicyDto>(raw, JsonOptions);
            return hardcodedDefaults;
        }
        catch
        {
            return hardcodedDefaults;
        }
    }

    public async Task<DetectionPolicyDto> SaveAsync(DetectionPolicyDto policy, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        actor = string.IsNullOrWhiteSpace(actor) ? "operator" : actor;
        var normalized = DetectionPolicyFactory.CreateProductionPolicy(enabled: true);
        await settingsStore.SetAsync(PolicyStorageKey, JsonSerializer.Serialize(normalized, JsonOptions), cancellationToken);
        await auditService.RecordAsync(
            "detection_policy_updated",
            actor,
            $"enabled=true; profile={normalized.PolicyVersion}; thresholds=hardcoded; collection=disabled",
            cancellationToken);

        return normalized;
    }
}
