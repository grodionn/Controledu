using Controledu.Common.Runtime;
using Controledu.Detection.Abstractions;
using Controledu.Transport.Dto;
using System.Text.Json;

namespace Controledu.Student.Agent.Services;

/// <summary>
/// Optional local dataset collector for future model training.
/// </summary>
public sealed class DatasetCollectionService(ILogger<DatasetCollectionService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _sync = new();
    private DateTimeOffset _lastCollectedAtUtc = DateTimeOffset.MinValue;

    /// <summary>
    /// Captures one sample if data-collection policy allows it.
    /// </summary>
    public async Task TryCollectAsync(
        DetectionObservation observation,
        DetectionResult result,
        DetectionPolicyDto policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(policy);

        if (!policy.DataCollectionModeEnabled)
        {
            return;
        }

        if (!observation.FrameChanged || observation.FrameBytes is null || observation.FrameBytes.Length == 0)
        {
            return;
        }

        if (Random.Shared.NextDouble() > Math.Clamp(policy.DataCollectionSampleRate, 0, 1))
        {
            return;
        }

        lock (_sync)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(1, policy.DataCollectionMinIntervalSeconds));
            if (observation.TimestampUtc - _lastCollectedAtUtc < interval)
            {
                return;
            }

            _lastCollectedAtUtc = observation.TimestampUtc;
        }

        var datasetRoot = Path.Combine(AppPaths.GetDatasetsPath(), "dataset");
        var rawRoot = Path.Combine(datasetRoot, "raw", observation.StudentId);
        var labelsRoot = Path.Combine(datasetRoot, "labels");
        var splitsRoot = Path.Combine(datasetRoot, "splits");

        Directory.CreateDirectory(rawRoot);
        Directory.CreateDirectory(labelsRoot);
        Directory.CreateDirectory(splitsRoot);

        var timestamp = observation.TimestampUtc.ToString("yyyy-MM-ddTHH-mm-ssZ", System.Globalization.CultureInfo.InvariantCulture);
        var baseName = timestamp + "-" + Guid.NewGuid().ToString("N")[..8];
        var imagePath = Path.Combine(rawRoot, baseName + ".jpg");
        var metaPath = Path.Combine(rawRoot, baseName + ".json");

        var payload = observation.FrameBytes;
        if (!policy.DataCollectionStoreFullFrames && policy.DataCollectionStoreThumbnails && observation.OptionalThumbnailBytes is { Length: > 0 })
        {
            payload = observation.OptionalThumbnailBytes;
        }

        await File.WriteAllBytesAsync(imagePath, payload, cancellationToken);

        var metadata = new
        {
            observation.StudentId,
            observation.TimestampUtc,
            observation.ScreenFrameHash,
            observation.FrameChanged,
            observation.ActiveProcessName,
            observation.ActiveWindowTitle,
            observation.BrowserHintUrl,
            detection = new
            {
                result.IsAiUiDetected,
                result.Confidence,
                Class = result.Class.ToString(),
                StageSource = result.StageSource.ToString(),
                result.Reason,
                result.ModelVersion,
                result.IsStable,
                result.TriggeredKeywords,
            }
        };

        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(metadata, JsonOptions), cancellationToken);
        await ApplyRetentionAsync(rawRoot, policy.DataCollectionRetentionDays, cancellationToken);

        logger.LogInformation("Collected dataset sample {ImagePath}", imagePath);
    }

    /// <summary>
    /// Exports local dataset folder to ZIP archive.
    /// </summary>
    public Task<string> ExportDatasetAsync(CancellationToken cancellationToken = default)
    {
        var datasetRoot = Path.Combine(AppPaths.GetDatasetsPath(), "dataset");
        Directory.CreateDirectory(datasetRoot);

        var exportName = $"dataset-export-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip";
        var exportPath = Path.Combine(AppPaths.GetExportsPath(), exportName);

        if (File.Exists(exportPath))
        {
            File.Delete(exportPath);
        }

        System.IO.Compression.ZipFile.CreateFromDirectory(datasetRoot, exportPath, System.IO.Compression.CompressionLevel.Fastest, includeBaseDirectory: true);
        return Task.FromResult(exportPath);
    }

    private static Task ApplyRetentionAsync(string rawRoot, int retentionDays, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, retentionDays));
        foreach (var file in Directory.EnumerateFiles(rawRoot))
        {
            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(file);
                if (lastWrite < cutoff.UtcDateTime)
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        return Task.CompletedTask;
    }
}
