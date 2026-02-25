using System.Net.Http.Json;
using System.Text.Json;
using Controledu.Student.Agent.Options;
using Controledu.Transport.Dto;
using Microsoft.Extensions.Options;

namespace Controledu.Student.Agent.Services;

/// <summary>
/// Calls loopback Student.Host API for local UI/profile actions.
/// </summary>
public interface IStudentLocalHostClient
{
    /// <summary>
    /// Applies teacher-assigned accessibility profile on local student host.
    /// </summary>
    Task<bool> TryApplyTeacherAccessibilityProfileAsync(AccessibilityProfileAssignmentCommandDto command, CancellationToken cancellationToken);

    /// <summary>
    /// Returns whether teacher TTS is enabled by local accessibility profile, or null when unavailable.
    /// </summary>
    Task<bool?> TryGetTeacherTtsEnabledAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves teacher chat message into local student-host chat timeline.
    /// </summary>
    Task<bool> TryDeliverTeacherChatMessageAsync(StudentTeacherChatMessageDto message, CancellationToken cancellationToken);

    /// <summary>
    /// Pushes teacher live caption update into local student-host overlay state.
    /// </summary>
    Task<bool> TryDeliverTeacherLiveCaptionAsync(TeacherLiveCaptionCommandDto caption, CancellationToken cancellationToken);

    /// <summary>
    /// Returns locally queued outgoing student chat messages from Student.Host without removing them.
    /// </summary>
    Task<IReadOnlyList<StudentTeacherChatMessageDto>> TryPeekStudentChatOutboxAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Acknowledges successfully delivered student chat messages so Student.Host can remove them from outbox.
    /// </summary>
    Task<bool> TryAckStudentChatOutboxAsync(IReadOnlyList<string> messageIds, CancellationToken cancellationToken);
}

internal sealed class StudentLocalHostClient(
    IHttpClientFactory httpClientFactory,
    IOptions<StudentAgentOptions> options,
    ILogger<StudentLocalHostClient> logger) : IStudentLocalHostClient
{
    private const string LocalApiTokenHeader = "X-Controledu-LocalToken";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> TryApplyTeacherAccessibilityProfileAsync(AccessibilityProfileAssignmentCommandDto command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            var baseUrl = $"http://127.0.0.1:{options.Value.LocalHostPort}";

            var token = await GetSessionTokenAsync(http, baseUrl, cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                logger.LogWarning("Unable to get local Student.Host session token for accessibility profile assignment.");
                return false;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/accessibility/profile/teacher-assign")
            {
                Content = JsonContent.Create(new
                {
                    teacherDisplayName = command.TeacherDisplayName,
                    profile = command.Profile,
                }, options: JsonOptions),
            };

            request.Headers.TryAddWithoutValidation(LocalApiTokenHeader, token);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "Local Student.Host rejected teacher accessibility profile command ({StatusCode}): {Body}",
                    (int)response.StatusCode,
                    string.IsNullOrWhiteSpace(body) ? "-" : body);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Failed to apply teacher accessibility profile on local Student.Host.");
            return false;
        }
    }

    public async Task<bool?> TryGetTeacherTtsEnabledAsync(CancellationToken cancellationToken)
    {
        try
        {
            var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            var baseUrl = $"http://127.0.0.1:{options.Value.LocalHostPort}";

            var token = await GetSessionTokenAsync(http, baseUrl, cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/accessibility/profile");
            request.Headers.TryAddWithoutValidation(LocalApiTokenHeader, token);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<AccessibilityProfileDto>(JsonOptions, cancellationToken);
            return payload?.Features?.TtsTeacherMessagesEnabled;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogDebug(ex, "Unable to read local accessibility profile for TTS flag.");
            return null;
        }
    }

    public async Task<bool> TryDeliverTeacherChatMessageAsync(StudentTeacherChatMessageDto message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            var baseUrl = $"http://127.0.0.1:{options.Value.LocalHostPort}";
            var token = await GetSessionTokenAsync(http, baseUrl, cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/chat/messages/teacher")
            {
                Content = JsonContent.Create(message, options: JsonOptions),
            };
            request.Headers.TryAddWithoutValidation(LocalApiTokenHeader, token);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Student.Host rejected teacher chat message bridge with status {StatusCode}.", (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogDebug(ex, "Unable to deliver teacher chat message to Student.Host.");
            return false;
        }
    }

    public async Task<bool> TryDeliverTeacherLiveCaptionAsync(TeacherLiveCaptionCommandDto caption, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caption);

        try
        {
            var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            var baseUrl = $"http://127.0.0.1:{options.Value.LocalHostPort}";
            var token = await GetSessionTokenAsync(http, baseUrl, cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/captions/live/teacher")
            {
                Content = JsonContent.Create(caption, options: JsonOptions),
            };
            request.Headers.TryAddWithoutValidation(LocalApiTokenHeader, token);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Student.Host rejected teacher live caption bridge with status {StatusCode}.", (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogDebug(ex, "Unable to deliver teacher live caption to Student.Host.");
            return false;
        }
    }

    public async Task<IReadOnlyList<StudentTeacherChatMessageDto>> TryPeekStudentChatOutboxAsync(CancellationToken cancellationToken)
    {
        try
        {
            var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            var baseUrl = $"http://127.0.0.1:{options.Value.LocalHostPort}";
            var token = await GetSessionTokenAsync(http, baseUrl, cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                return [];
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/chat/outgoing/peek");
            request.Headers.TryAddWithoutValidation(LocalApiTokenHeader, token);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var payload = await response.Content.ReadFromJsonAsync<ChatOutboxPeekDto>(JsonOptions, cancellationToken);
            return payload?.Messages ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogDebug(ex, "Unable to peek student chat outbox from Student.Host.");
            return [];
        }
    }

    public async Task<bool> TryAckStudentChatOutboxAsync(IReadOnlyList<string> messageIds, CancellationToken cancellationToken)
    {
        if (messageIds is null || messageIds.Count == 0)
        {
            return true;
        }

        try
        {
            var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            var baseUrl = $"http://127.0.0.1:{options.Value.LocalHostPort}";
            var token = await GetSessionTokenAsync(http, baseUrl, cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/chat/outgoing/ack")
            {
                Content = JsonContent.Create(new { messageIds }, options: JsonOptions),
            };
            request.Headers.TryAddWithoutValidation(LocalApiTokenHeader, token);
            using var response = await http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogDebug(ex, "Unable to ack student chat outbox in Student.Host.");
            return false;
        }
    }

    private static async Task<string?> GetSessionTokenAsync(HttpClient http, string baseUrl, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"{baseUrl}/api/session", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<SessionTokenDto>(JsonOptions, cancellationToken);
        return payload?.Token;
    }

    private sealed record SessionTokenDto(string Token);
    private sealed record AccessibilityProfileDto(AccessibilityFeatureFlagsDto? Features);
    private sealed record AccessibilityFeatureFlagsDto(bool TtsTeacherMessagesEnabled);
    private sealed record ChatOutboxPeekDto(IReadOnlyList<StudentTeacherChatMessageDto> Messages);
}
