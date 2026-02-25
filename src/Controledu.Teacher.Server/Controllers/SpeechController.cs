using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Controledu.Teacher.Server.Controllers;

/// <summary>
/// Teacher-side speech proxy endpoints (self-host STT).
/// </summary>
[ApiController]
[Route("api/speech")]
public sealed class SpeechController(IHttpClientFactory httpClientFactory) : ControllerBase
{
    /// <summary>
    /// Proxies microphone audio chunk to self-host speech service STT endpoint.
    /// </summary>
    [HttpPost("stt/transcribe")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> TranscribeSelfHostStt(
        [FromForm] TeacherSttProxyRequest request,
        CancellationToken cancellationToken)
    {
        if (request.File is null || request.File.Length <= 0)
        {
            return BadRequest("Audio file is required.");
        }

        var baseUrl = (request.SelfHostBaseUrl ?? "https://tts.kilocraft.org").Trim();
        var token = (request.SelfHostApiToken ?? string.Empty).Trim();
        var path = string.IsNullOrWhiteSpace(request.SelfHostSttPath) ? "/v1/stt/transcribe" : request.SelfHostSttPath.Trim();

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token))
        {
            return BadRequest("Self-host STT token and URL are required.");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return BadRequest("Self-host STT URL is invalid.");
        }

        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        var endpoint = new Uri(baseUri, path);
        var normalizedTask = string.Equals(request.Task, "translate", StringComparison.OrdinalIgnoreCase) ? "translate" : "transcribe";
        var normalizedLanguage = string.IsNullOrWhiteSpace(request.LanguageCode) || string.Equals(request.LanguageCode, "auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : request.LanguageCode.Trim();

        var http = httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(45);

        await using var audioStream = request.File.OpenReadStream();
        using var multipart = new MultipartFormDataContent();
        var audioContent = new StreamContent(audioStream);
        audioContent.Headers.ContentType = MediaTypeHeaderValue.TryParse(request.File.ContentType, out var parsedContentType)
            ? parsedContentType
            : new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(audioContent, "file", string.IsNullOrWhiteSpace(request.File.FileName) ? "audio.webm" : request.File.FileName);
        multipart.Add(new StringContent(normalizedTask), "task");
        multipart.Add(new StringContent("true"), "vad_filter");
        multipart.Add(new StringContent("false"), "word_timestamps");
        if (!string.IsNullOrWhiteSpace(normalizedLanguage))
        {
            multipart.Add(new StringContent(normalizedLanguage), "language");
        }

        using var upstream = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = multipart,
        };
        upstream.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await http.SendAsync(upstream, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                return StatusCode(
                    StatusCodes.Status502BadGateway,
                    new { ok = false, detail = string.IsNullOrWhiteSpace(errorBody) ? $"Self-host STT failed ({(int)response.StatusCode})." : errorBody });
            }

            var payload = await response.Content.ReadFromJsonAsync<SelfHostSttTranscribeResponse>(cancellationToken: cancellationToken);
            if (payload is null)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { ok = false, detail = "Self-host STT returned invalid JSON." });
            }

            return Ok(new TeacherSttTranscribeResponse(
                Ok: true,
                Text: (payload.Text ?? string.Empty).Trim(),
                Language: payload.Language,
                Task: payload.Task ?? normalizedTask,
                Duration: payload.Duration,
                DurationAfterVad: payload.DurationAfterVad));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { ok = false, detail = $"Self-host STT request error: {ex.Message}" });
        }
    }
}

/// <summary>
/// Form payload for proxying audio chunk to self-host STT endpoint.
/// </summary>
public sealed class TeacherSttProxyRequest
{
    public IFormFile? File { get; init; }
    public string? LanguageCode { get; init; }
    public string? Task { get; init; }
    public string? SelfHostBaseUrl { get; init; }
    public string? SelfHostApiToken { get; init; }
    public string? SelfHostSttPath { get; init; }
}

/// <summary>
/// Simplified teacher-facing STT proxy response.
/// </summary>
public sealed record TeacherSttTranscribeResponse(
    bool Ok,
    string Text,
    string? Language,
    string Task,
    double? Duration,
    double? DurationAfterVad);

internal sealed record SelfHostSttTranscribeResponse(
    string Text,
    string? Language,
    [property: JsonPropertyName("language_probability")] double? LanguageProbability,
    double? Duration,
    [property: JsonPropertyName("duration_after_vad")] double? DurationAfterVad,
    string? Model,
    string? Task);
