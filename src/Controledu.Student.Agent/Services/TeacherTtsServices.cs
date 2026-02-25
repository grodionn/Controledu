using System.Media;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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

        if (string.Equals(provider, "google", StringComparison.OrdinalIgnoreCase))
        {
            return await TrySynthesizeGoogleAsync(command, text, ttsOptions, cancellationToken);
        }

        if (string.Equals(provider, "selfhost", StringComparison.OrdinalIgnoreCase))
        {
            return await TrySynthesizeSelfHostAsync(command, text, ttsOptions, cancellationToken);
        }

        logger.LogWarning("Unsupported TTS provider '{Provider}'.", provider);
        return null;
    }

    private async Task<byte[]?> TrySynthesizeGoogleAsync(
        TeacherTtsCommandDto command,
        string text,
        StudentTeacherTtsOptions ttsOptions,
        CancellationToken cancellationToken)
    {
        var apiKey = (ttsOptions.GoogleApiKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("TeacherTts provider=google but GoogleApiKey is not configured.");
            return null;
        }

        var languageCode = ResolveLanguageCode(command, ttsOptions);
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

    private async Task<byte[]?> TrySynthesizeSelfHostAsync(
        TeacherTtsCommandDto command,
        string text,
        StudentTeacherTtsOptions ttsOptions,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildSelfHostEndpoint(ttsOptions);
        if (endpoint is null)
        {
            logger.LogWarning("TeacherTts provider=selfhost but SelfHostBaseUrl/SelfHostTtsPath are invalid or missing.");
            return null;
        }

        var token = (ttsOptions.SelfHostApiToken ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("TeacherTts provider=selfhost but SelfHostApiToken is not configured.");
            return null;
        }

        var languageCode = ResolveLanguageCode(command, ttsOptions);
        var voice = ResolveSelfHostVoice(command, ttsOptions, languageCode);
        var speakingRate = Math.Clamp(command.SpeakingRate ?? ttsOptions.SpeakingRate, 0.25, 4.0);
        var lengthScale = ToPiperLengthScale(speakingRate);

        var requestBody = new SelfHostTtsRequest(
            Text: text,
            Voice: voice,
            LengthScale: lengthScale,
            OutputFormat: "wav");

        try
        {
            var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(Math.Clamp(ttsOptions.SelfHostTimeoutSeconds, 3, 120));

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/wav"));
            request.Content = JsonContent.Create(requestBody, options: JsonOptions);

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "Self-host TTS request failed ({StatusCode}) {Endpoint}: {Body}",
                    (int)response.StatusCode,
                    endpoint,
                    string.IsNullOrWhiteSpace(errorBody) ? "-" : errorBody);
                return null;
            }

            var audioBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (audioBytes.Length == 0)
            {
                logger.LogWarning("Self-host TTS returned empty audio content from {Endpoint}.", endpoint);
                return null;
            }

            return audioBytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Self-host TTS synthesis failed via {Endpoint}.", endpoint);
            return null;
        }
    }

    private static string ResolveLanguageCode(TeacherTtsCommandDto command, StudentTeacherTtsOptions ttsOptions)
    {
        var languageCode = string.IsNullOrWhiteSpace(command.LanguageCode) ? ttsOptions.LanguageCode : command.LanguageCode.Trim();
        return string.IsNullOrWhiteSpace(languageCode) ? "ru-RU" : languageCode;
    }

    private static Uri? BuildSelfHostEndpoint(StudentTeacherTtsOptions ttsOptions)
    {
        var baseUrl = (ttsOptions.SelfHostBaseUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return null;
        }

        var path = string.IsNullOrWhiteSpace(ttsOptions.SelfHostTtsPath) ? "/v1/tts/synthesize" : ttsOptions.SelfHostTtsPath.Trim();
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return new Uri(baseUri, path);
    }

    private static string? ResolveSelfHostVoice(
        TeacherTtsCommandDto command,
        StudentTeacherTtsOptions ttsOptions,
        string languageCode)
    {
        var explicitVoice = SanitizePiperVoiceName(command.VoiceName);
        if (!string.IsNullOrWhiteSpace(explicitVoice))
        {
            return explicitVoice;
        }

        var normalizedLang = languageCode.Trim().ToLowerInvariant();
        var mappedVoice = normalizedLang.StartsWith("kk") || normalizedLang.StartsWith("kz")
            ? ttsOptions.SelfHostKazakhVoice
            : normalizedLang.StartsWith("ru")
                ? ttsOptions.SelfHostRussianVoice
                : normalizedLang.StartsWith("en")
                    ? ttsOptions.SelfHostEnglishVoice
                    : null;

        return SanitizePiperVoiceName(mappedVoice)
            ?? SanitizePiperVoiceName(ttsOptions.SelfHostDefaultVoice)
            ?? SanitizePiperVoiceName(ttsOptions.VoiceName);
    }

    private static string? SanitizePiperVoiceName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();
        if (value.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^5];
        }

        if (value.Contains("/") || value.Contains("\\") || value.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        // Piper voice basenames usually contain locale prefix like ru_RU-... ; ignore cloud voice ids (ru-RU-Wavenet-A).
        if (!value.Contains('_'))
        {
            return null;
        }

        return value;
    }

    private static double? ToPiperLengthScale(double speakingRate)
    {
        var normalized = Math.Clamp(speakingRate, 0.25, 4.0);
        if (Math.Abs(normalized - 1.0) < 0.001)
        {
            return null;
        }

        // Piper uses lower length_scale for faster speech, inverse of "rate" slider semantics.
        return Math.Clamp(1.0 / normalized, 0.35, 2.5);
    }

    private sealed record GoogleTtsRequest(GoogleTtsInput Input, GoogleTtsVoice Voice, GoogleTtsAudioConfig AudioConfig);
    private sealed record GoogleTtsInput(string Text);
    private sealed record GoogleTtsVoice(string LanguageCode, string? Name);
    private sealed record GoogleTtsAudioConfig(string AudioEncoding, double SpeakingRate, double Pitch);
    private sealed record GoogleTtsResponse(string? AudioContent);

    private sealed record SelfHostTtsRequest(
        string Text,
        string? Voice,
        [property: JsonPropertyName("length_scale")] double? LengthScale,
        [property: JsonPropertyName("output_format")] string OutputFormat);
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

