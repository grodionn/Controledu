using System.Media;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Controledu.Student.Agent.Options;
using Controledu.Transport.Dto;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Controledu.Student.Agent.Services;

/// <summary>
/// Synthesizes teacher TTS commands into playable audio.
/// </summary>
public interface ITeacherTtsSynthesisService
{
    /// <summary>
    /// Returns LINEAR16/WAV audio bytes for a TTS command, or null when disabled/unavailable.
    /// </summary>
    Task<byte[]?> TrySynthesizeAsync(TeacherTtsCommandDto command, CancellationToken cancellationToken);
}

/// <summary>
/// Async queue for audio playback so the main agent worker is not blocked by sound duration.
/// </summary>
public interface ITeacherTtsPlaybackQueue
{
    /// <summary>
    /// Enqueues teacher TTS audio for local playback.
    /// </summary>
    ValueTask QueueAsync(QueuedTeacherTtsAudio item, CancellationToken cancellationToken);
}

/// <summary>
/// Queued audio payload for teacher TTS playback.
/// </summary>
public sealed record QueuedTeacherTtsAudio(
    byte[] WavBytes,
    string MessageText,
    string? TeacherDisplayName,
    string RequestId);

internal sealed class TeacherTtsSynthesisService(
    IHttpClientFactory httpClientFactory,
    IOptions<StudentAgentOptions> options,
    ILogger<TeacherTtsSynthesisService> logger) : ITeacherTtsSynthesisService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<byte[]?> TrySynthesizeAsync(TeacherTtsCommandDto command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var ttsOptions = options.Value.TeacherTts ?? new StudentTeacherTtsOptions();
        if (!ttsOptions.Enabled)
        {
            return null;
        }

        var provider = (ttsOptions.Provider ?? "google").Trim();
        if (string.Equals(provider, "disabled", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!string.Equals(provider, "google", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Unsupported TTS provider '{Provider}'.", provider);
            return null;
        }

        var text = (command.MessageText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var maxTextLength = Math.Clamp(ttsOptions.MaxTextLength, 32, 4000);
        if (text.Length > maxTextLength)
        {
            text = text[..maxTextLength];
        }

        var apiKey = (ttsOptions.GoogleApiKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("TeacherTts is enabled but GoogleApiKey is not configured.");
            return null;
        }

        var languageCode = string.IsNullOrWhiteSpace(command.LanguageCode) ? ttsOptions.LanguageCode : command.LanguageCode.Trim();
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            languageCode = "ru-RU";
        }

        var voiceName = string.IsNullOrWhiteSpace(command.VoiceName) ? ttsOptions.VoiceName : command.VoiceName.Trim();
        var speakingRate = Math.Clamp(command.SpeakingRate ?? ttsOptions.SpeakingRate, 0.25, 4.0);
        var pitch = Math.Clamp(command.Pitch ?? ttsOptions.Pitch, -20.0, 20.0);

        var requestBody = new GoogleTtsRequest(
            Input: new GoogleTtsInput(text),
            Voice: new GoogleTtsVoice(languageCode, voiceName),
            AudioConfig: new GoogleTtsAudioConfig("LINEAR16", speakingRate, pitch));

        try
        {
            var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);

            using var response = await http.PostAsJsonAsync(
                $"https://texttospeech.googleapis.com/v1/text:synthesize?key={Uri.EscapeDataString(apiKey)}",
                requestBody,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "Google TTS request failed ({StatusCode}): {Body}",
                    (int)response.StatusCode,
                    string.IsNullOrWhiteSpace(errorBody) ? "-" : errorBody);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<GoogleTtsResponse>(JsonOptions, cancellationToken);
            if (payload is null || string.IsNullOrWhiteSpace(payload.AudioContent))
            {
                logger.LogWarning("Google TTS returned empty audio content.");
                return null;
            }

            return Convert.FromBase64String(payload.AudioContent);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException)
        {
            logger.LogWarning(ex, "Google TTS synthesis failed.");
            return null;
        }
    }

    private sealed record GoogleTtsRequest(GoogleTtsInput Input, GoogleTtsVoice Voice, GoogleTtsAudioConfig AudioConfig);
    private sealed record GoogleTtsInput(string Text);
    private sealed record GoogleTtsVoice(string LanguageCode, string? Name);
    private sealed record GoogleTtsAudioConfig(string AudioEncoding, double SpeakingRate, double Pitch);
    private sealed record GoogleTtsResponse(string? AudioContent);
}

internal sealed class TeacherTtsPlaybackService(ILogger<TeacherTtsPlaybackService> logger) : BackgroundService, ITeacherTtsPlaybackQueue
{
    private readonly Channel<QueuedTeacherTtsAudio> _queue = Channel.CreateUnbounded<QueuedTeacherTtsAudio>();

    public ValueTask QueueAsync(QueuedTeacherTtsAudio item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _queue.Writer.WriteAsync(item, cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var item = await _queue.Reader.ReadAsync(stoppingToken);
                await PlayAsync(item, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Teacher TTS playback queue loop error.");
            }
        }
    }

    private async Task PlayAsync(QueuedTeacherTtsAudio item, CancellationToken cancellationToken)
    {
        if (item.WavBytes.Length == 0)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            logger.LogWarning("Teacher TTS playback is only implemented for Windows endpoints.");
            return;
        }

        logger.LogInformation(
            "Playing teacher TTS message ({RequestId}) from {TeacherDisplayName}: {Preview}",
            item.RequestId,
            string.IsNullOrWhiteSpace(item.TeacherDisplayName) ? "Teacher" : item.TeacherDisplayName,
            item.MessageText.Length > 64 ? item.MessageText[..64] : item.MessageText);

        await Task.Run(() =>
        {
            using var stream = new MemoryStream(item.WavBytes, writable: false);
            using var player = new SoundPlayer(stream);
            player.Load();
            player.PlaySync();
        }, cancellationToken);
    }
}

