using Controledu.Student.Agent.Models;
using System.Net.Http.Headers;

namespace Controledu.Student.Agent.Services;

/// <summary>
/// Uploads student-side diagnostics/dataset export archives to teacher server.
/// </summary>
public sealed class DiagnosticsExportUploader(IHttpClientFactory httpClientFactory, ILogger<DiagnosticsExportUploader> logger)
{
    /// <summary>
    /// Uploads a ZIP export archive to teacher server.
    /// </summary>
    public async Task UploadAsync(ResolvedStudentBinding binding, string archivePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            throw new FileNotFoundException("Diagnostics export archive was not found.", archivePath);
        }

        using var http = httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(5);

        await using var fileStream = File.OpenRead(archivePath);
        using var content = new StreamContent(fileStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

        var uploadUri = $"{binding.ServerBaseUrl.TrimEnd('/')}/api/detection/exports/upload?clientId={Uri.EscapeDataString(binding.ClientId)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUri)
        {
            Content = content,
        };

        request.Headers.TryAddWithoutValidation("X-Controledu-Token", binding.Token);
        request.Headers.TryAddWithoutValidation("X-Controledu-FileName", Path.GetFileName(archivePath));

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(body)
                ? $"Diagnostics upload failed ({(int)response.StatusCode})."
                : body);
        }

        logger.LogInformation("Diagnostics export uploaded for client {ClientId}: {ArchivePath}", binding.ClientId, archivePath);
    }
}

