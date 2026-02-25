using System.Text.Json.Serialization;

namespace Controledu.Common.Updates;

/// <summary>
/// Describes the latest installer available for a product.
/// </summary>
public sealed class AutoUpdateManifest
{
    [JsonPropertyName("product")]
    public string Product { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("installerUrl")]
    public string InstallerUrl { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("mandatory")]
    public bool Mandatory { get; set; } = true;

    [JsonPropertyName("publishedAtUtc")]
    public DateTimeOffset PublishedAtUtc { get; set; }

    [JsonPropertyName("releaseNotes")]
    public string? ReleaseNotes { get; set; }

    /// <summary>
    /// Normalizes and validates manifest fields.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Product))
        {
            throw new InvalidOperationException("Update manifest is missing product.");
        }

        if (string.IsNullOrWhiteSpace(Version))
        {
            throw new InvalidOperationException("Update manifest is missing version.");
        }

        if (!Uri.TryCreate(InstallerUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Update manifest contains invalid installerUrl.");
        }

        Sha256 = Sha256.Trim().ToLowerInvariant();
        if (Sha256.Length != 64 || Sha256.Any(static c => !Uri.IsHexDigit(c)))
        {
            throw new InvalidOperationException("Update manifest contains invalid sha256.");
        }

        if (SizeBytes < 1)
        {
            throw new InvalidOperationException("Update manifest contains invalid sizeBytes.");
        }
    }
}
