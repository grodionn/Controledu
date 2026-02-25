using Controledu.Common.Runtime;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace Controledu.Common.Updates;

/// <summary>
/// Handles manifest retrieval and installer download/verification.
/// </summary>
public sealed class AutoUpdateClient(HttpClient httpClient) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<AutoUpdateManifest> FetchManifestAsync(string manifestUrl, int timeoutSeconds, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl))
        {
            throw new InvalidOperationException("AutoUpdate manifest URL is not configured.");
        }

        using var cts = CreateTimeoutCts(timeoutSeconds, cancellationToken);
        using var response = await _httpClient.GetAsync(manifestUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();

        var manifest = await response.Content.ReadFromJsonAsync<AutoUpdateManifest>(JsonOptions, cts.Token)
            ?? throw new InvalidOperationException("Update manifest response is empty.");
        manifest.Validate();
        return manifest;
    }

    public bool IsUpdateAvailable(string currentVersion, AutoUpdateManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return VersionTextComparer.IsNewer(currentVersion, manifest.Version);
    }

    public async Task<string> DownloadInstallerAsync(
        AutoUpdateManifest manifest,
        string productCacheKey,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (!Uri.TryCreate(manifest.InstallerUrl, UriKind.Absolute, out var installerUri))
        {
            throw new InvalidOperationException("Manifest installerUrl is invalid.");
        }

        var updatesRoot = Path.Combine(AppPaths.GetBasePath(), "updates", SanitizePathSegment(productCacheKey), SanitizePathSegment(manifest.Version));
        Directory.CreateDirectory(updatesRoot);

        var fileName = Path.GetFileName(installerUri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "installer.exe";
        }

        var destinationPath = Path.Combine(updatesRoot, fileName);
        if (File.Exists(destinationPath))
        {
            var existingHash = await ComputeSha256Async(destinationPath, cancellationToken);
            if (string.Equals(existingHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return destinationPath;
            }

            File.Delete(destinationPath);
        }

        var tempPath = destinationPath + ".part";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        using var cts = CreateTimeoutCts(timeoutSeconds, cancellationToken);
        using (var response = await _httpClient.GetAsync(installerUri, HttpCompletionOption.ResponseHeadersRead, cts.Token))
        {
            response.EnsureSuccessStatusCode();
            await using var network = await response.Content.ReadAsStreamAsync(cts.Token);
            await using var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 64, useAsync: true);
            await network.CopyToAsync(file, cts.Token);
        }

        var hash = await ComputeSha256Async(tempPath, cancellationToken);
        if (!string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(tempPath);
            throw new InvalidOperationException("Downloaded installer SHA-256 does not match manifest.");
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        File.Move(tempPath, destinationPath);
        return destinationPath;
    }

    public void Dispose()
    {
        // HttpClient is owned by the caller.
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 64, useAsync: true);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static CancellationTokenSource CreateTimeoutCts(int timeoutSeconds, CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var safeTimeout = Math.Clamp(timeoutSeconds, 10, 60 * 60);
        cts.CancelAfter(TimeSpan.FromSeconds(safeTimeout));
        return cts;
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim();
        if (chars.Length == 0)
        {
            return "default";
        }

        var output = new char[chars.Length];
        for (var i = 0; i < chars.Length; i++)
        {
            output[i] = invalid.Contains(chars[i]) ? '_' : chars[i];
        }

        return new string(output);
    }
}
