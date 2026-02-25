using System.Globalization;
using System.Text.Json;
using Controledu.Common.Runtime;
using Controledu.Student.Host.Contracts;
using Controledu.Storage.Stores;
using Controledu.Transport.Dto;

namespace Controledu.Student.Host.Services;

/// <summary>
/// Persists local teacher-student chat timeline, outbox queue, and overlay preferences.
/// </summary>
public interface IStudentChatService
{
    /// <summary>
    /// Returns current local thread snapshot for overlay UI.
    /// </summary>
    Task<StudentChatThreadResponse> GetThreadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues student message for agent delivery and stores it in local history.
    /// </summary>
    Task<StudentChatMessageResponse> QueueStudentMessageAsync(StudentChatSendRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores teacher-delivered message in local history.
    /// </summary>
    Task<StudentChatMessageResponse?> ReceiveTeacherMessageAsync(TeacherChatLocalDeliveryRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns outgoing student chat messages without removing them.
    /// </summary>
    Task<IReadOnlyList<StudentTeacherChatMessageDto>> PeekOutgoingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes outgoing chat messages that were successfully delivered by Student.Agent.
    /// </summary>
    Task<int> AcknowledgeOutgoingAsync(IReadOnlyList<string> messageIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates overlay chat preferences.
    /// </summary>
    Task<StudentChatPreferencesResponse> UpdatePreferencesAsync(StudentChatPreferencesUpdateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears local chat timeline and pending outgoing queue while preserving chat UI preferences.
    /// </summary>
    Task ClearThreadAsync(CancellationToken cancellationToken = default);
}

internal sealed class StudentChatService(
    ISettingsStore settingsStore,
    IStudentBindingStore bindingStore,
    IDeviceIdentityService deviceIdentityService) : IStudentChatService
{
    private const int MaxHistoryMessages = 200;
    private const int MaxOutboxMessages = 40;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _sync = new(1, 1);

    public async Task<StudentChatThreadResponse> GetThreadAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var clientId = await ResolveClientIdAsync(cancellationToken);
            var history = await LoadHistoryAsync(cancellationToken);
            var prefs = await LoadPreferencesAsync(cancellationToken);
            return new StudentChatThreadResponse(
                clientId,
                new StudentChatPreferencesResponse(prefs.FontScalePercent),
                history
                    .OrderBy(x => x.TimestampUtc)
                    .Select(MapToResponse)
                    .ToArray());
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<StudentChatMessageResponse> QueueStudentMessageAsync(StudentChatSendRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var text = (request.Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            throw new InvalidOperationException("Chat text is required.");
        }

        if (text.Length > 2000)
        {
            throw new InvalidOperationException("Chat message is too long.");
        }

        await _sync.WaitAsync(cancellationToken);
        try
        {
            var clientId = await ResolveClientIdAsync(cancellationToken);
            var deviceName = await deviceIdentityService.GetDisplayNameAsync(cancellationToken);
            var nowUtc = DateTimeOffset.UtcNow;
            var message = new LocalChatMessage(
                MessageId: Guid.NewGuid().ToString("N"),
                ClientId: clientId,
                SenderRole: "student",
                SenderDisplayName: string.IsNullOrWhiteSpace(deviceName) ? Environment.MachineName : deviceName,
                Text: text,
                TimestampUtc: nowUtc);

            var history = await LoadHistoryAsync(cancellationToken);
            history.Add(message);
            TrimHistory(history);
            await SaveHistoryAsync(history, cancellationToken);

            var outbox = await LoadOutboxAsync(cancellationToken);
            outbox.Add(ToTransport(message));
            if (outbox.Count > MaxOutboxMessages)
            {
                outbox = outbox.Skip(Math.Max(0, outbox.Count - MaxOutboxMessages)).ToList();
            }

            await SaveOutboxAsync(outbox, cancellationToken);
            return MapToResponse(message);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<StudentChatMessageResponse?> ReceiveTeacherMessageAsync(TeacherChatLocalDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var text = (request.Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(request.TimestampUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestampUtc))
        {
            timestampUtc = DateTimeOffset.UtcNow;
        }

        await _sync.WaitAsync(cancellationToken);
        try
        {
            var history = await LoadHistoryAsync(cancellationToken);
            if (history.Any(x => string.Equals(x.MessageId, request.MessageId, StringComparison.Ordinal)))
            {
                return history
                    .Where(x => string.Equals(x.MessageId, request.MessageId, StringComparison.Ordinal))
                    .Select(MapToResponse)
                    .FirstOrDefault();
            }

            var message = new LocalChatMessage(
                MessageId: string.IsNullOrWhiteSpace(request.MessageId) ? Guid.NewGuid().ToString("N") : request.MessageId.Trim(),
                ClientId: string.IsNullOrWhiteSpace(request.ClientId) ? await ResolveClientIdAsync(cancellationToken) : request.ClientId.Trim(),
                SenderRole: "teacher",
                SenderDisplayName: string.IsNullOrWhiteSpace(request.SenderDisplayName) ? "Teacher" : request.SenderDisplayName.Trim(),
                Text: text,
                TimestampUtc: timestampUtc);

            history.Add(message);
            TrimHistory(history);
            await SaveHistoryAsync(history, cancellationToken);
            return MapToResponse(message);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<IReadOnlyList<StudentTeacherChatMessageDto>> PeekOutgoingAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var outbox = await LoadOutboxAsync(cancellationToken);
            if (outbox.Count == 0)
            {
                return [];
            }

            return outbox.ToArray();
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<int> AcknowledgeOutgoingAsync(IReadOnlyList<string> messageIds, CancellationToken cancellationToken = default)
    {
        if (messageIds is null || messageIds.Count == 0)
        {
            return 0;
        }

        var ids = new HashSet<string>(
            messageIds.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()),
            StringComparer.Ordinal);
        if (ids.Count == 0)
        {
            return 0;
        }

        await _sync.WaitAsync(cancellationToken);
        try
        {
            var outbox = await LoadOutboxAsync(cancellationToken);
            if (outbox.Count == 0)
            {
                return 0;
            }

            var before = outbox.Count;
            outbox = outbox.Where(x => !ids.Contains(x.MessageId)).ToList();
            var removed = before - outbox.Count;
            if (removed > 0)
            {
                await SaveOutboxAsync(outbox, cancellationToken);
            }

            return removed;
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<StudentChatPreferencesResponse> UpdatePreferencesAsync(StudentChatPreferencesUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var fontScalePercent = Math.Clamp(request.FontScalePercent, 80, 220);
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var prefs = new LocalChatPreferences(fontScalePercent);
            await settingsStore.SetAsync(DetectionSettingKeys.ChatPreferencesJson, JsonSerializer.Serialize(prefs, JsonOptions), cancellationToken);
            return new StudentChatPreferencesResponse(fontScalePercent);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task ClearThreadAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            await settingsStore.SetAsync(DetectionSettingKeys.ChatHistoryJson, "[]", cancellationToken);
            await settingsStore.SetAsync(DetectionSettingKeys.ChatOutgoingQueueJson, "[]", cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task<string> ResolveClientIdAsync(CancellationToken cancellationToken)
    {
        var binding = await bindingStore.GetAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(binding?.ClientId) ? "unpaired-endpoint" : binding.ClientId;
    }

    private async Task<List<LocalChatMessage>> LoadHistoryAsync(CancellationToken cancellationToken)
    {
        var raw = await settingsStore.GetAsync(DetectionSettingKeys.ChatHistoryJson, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<LocalChatMessage>>(raw, JsonOptions);
            return parsed ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task SaveHistoryAsync(List<LocalChatMessage> history, CancellationToken cancellationToken)
    {
        await settingsStore.SetAsync(
            DetectionSettingKeys.ChatHistoryJson,
            JsonSerializer.Serialize(history, JsonOptions),
            cancellationToken);
    }

    private async Task<List<StudentTeacherChatMessageDto>> LoadOutboxAsync(CancellationToken cancellationToken)
    {
        var raw = await settingsStore.GetAsync(DetectionSettingKeys.ChatOutgoingQueueJson, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<StudentTeacherChatMessageDto>>(raw, JsonOptions);
            return parsed ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task SaveOutboxAsync(IReadOnlyList<StudentTeacherChatMessageDto> outbox, CancellationToken cancellationToken)
    {
        await settingsStore.SetAsync(
            DetectionSettingKeys.ChatOutgoingQueueJson,
            JsonSerializer.Serialize(outbox, JsonOptions),
            cancellationToken);
    }

    private async Task<LocalChatPreferences> LoadPreferencesAsync(CancellationToken cancellationToken)
    {
        var raw = await settingsStore.GetAsync(DetectionSettingKeys.ChatPreferencesJson, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new LocalChatPreferences(100);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<LocalChatPreferences>(raw, JsonOptions);
            return parsed is null
                ? new LocalChatPreferences(100)
                : new LocalChatPreferences(Math.Clamp(parsed.FontScalePercent, 80, 220));
        }
        catch
        {
            return new LocalChatPreferences(100);
        }
    }

    private static StudentChatMessageResponse MapToResponse(LocalChatMessage message) =>
        new(
            message.MessageId,
            message.ClientId,
            message.SenderRole,
            message.SenderDisplayName,
            message.Text,
            message.TimestampUtc.ToString("O", CultureInfo.InvariantCulture));

    private static StudentTeacherChatMessageDto ToTransport(LocalChatMessage message) =>
        new(
            message.ClientId,
            message.MessageId,
            message.TimestampUtc,
            message.SenderRole,
            message.SenderDisplayName,
            message.Text);

    private static void TrimHistory(List<LocalChatMessage> history)
    {
        if (history.Count <= MaxHistoryMessages)
        {
            return;
        }

        history.RemoveRange(0, history.Count - MaxHistoryMessages);
    }

    private sealed record LocalChatMessage(
        string MessageId,
        string ClientId,
        string SenderRole,
        string SenderDisplayName,
        string Text,
        DateTimeOffset TimestampUtc);

    private sealed record LocalChatPreferences(int FontScalePercent);
}
