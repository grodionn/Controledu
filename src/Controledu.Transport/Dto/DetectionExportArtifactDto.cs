namespace Controledu.Transport.Dto;

/// <summary>
/// Metadata of a diagnostics/dataset export uploaded by a student agent.
/// </summary>
public sealed record DetectionExportArtifactDto(
    string ExportId,
    string ClientId,
    string StudentDisplayName,
    DateTimeOffset CreatedAtUtc,
    string FileName,
    long SizeBytes,
    string DownloadUrl);

