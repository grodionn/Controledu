using Controledu.Common.Runtime;
using Controledu.Storage.Stores;
using Controledu.Transport.Dto;
using System.Text.Json;

namespace Controledu.Student.Host.Services;

public interface IRemoteControlConsentService
{
    Task<RemoteControlConsentPrompt?> TryGetPendingPromptAsync(CancellationToken cancellationToken = default);
    Task SubmitDecisionAsync(RemoteControlApprovalDecisionDto decision, CancellationToken cancellationToken = default);
}

public sealed record RemoteControlConsentPrompt(
    string SessionId,
    string RequestedBy,
    int ApprovalTimeoutSeconds,
    int MaxSessionSeconds);

internal sealed class RemoteControlConsentService(ISettingsStore settingsStore) : IRemoteControlConsentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RemoteControlConsentPrompt?> TryGetPendingPromptAsync(CancellationToken cancellationToken = default)
    {
        var raw = await settingsStore.GetAsync(DetectionSettingKeys.RemoteControlRequestJson, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        RemoteControlSessionCommandDto? request;
        try
        {
            request = JsonSerializer.Deserialize<RemoteControlSessionCommandDto>(raw, JsonOptions);
        }
        catch
        {
            await settingsStore.SetAsync(DetectionSettingKeys.RemoteControlRequestJson, string.Empty, cancellationToken);
            return null;
        }

        if (request is null || request.Action != RemoteControlSessionAction.RequestStart)
        {
            return null;
        }

        var decisionRaw = await settingsStore.GetAsync(DetectionSettingKeys.RemoteControlDecisionJson, cancellationToken);
        if (!string.IsNullOrWhiteSpace(decisionRaw))
        {
            try
            {
                var existingDecision = JsonSerializer.Deserialize<RemoteControlApprovalDecisionDto>(decisionRaw, JsonOptions);
                if (existingDecision is not null && string.Equals(existingDecision.SessionId, request.SessionId, StringComparison.Ordinal))
                {
                    return null;
                }
            }
            catch
            {
                // Ignore malformed local decision payload, agent will clear it.
            }
        }

        return new RemoteControlConsentPrompt(
            request.SessionId,
            request.RequestedBy,
            request.ApprovalTimeoutSeconds,
            request.MaxSessionSeconds);
    }

    public Task SubmitDecisionAsync(RemoteControlApprovalDecisionDto decision, CancellationToken cancellationToken = default) =>
        settingsStore.SetAsync(DetectionSettingKeys.RemoteControlDecisionJson, JsonSerializer.Serialize(decision, JsonOptions), cancellationToken);
}
