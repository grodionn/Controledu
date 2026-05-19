using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Controledu.Common.Runtime;

/// <summary>
/// Creates support archives with local logs and runtime metadata.
/// </summary>
public static class DiagnosticsArchiveBuilder
{
    private const long MaxSingleFileBytes = 50L * 1024L * 1024L;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Creates a student diagnostics ZIP in the shared exports directory.
    /// </summary>
    public static string CreateStudentDiagnosticsArchive(
        IReadOnlyDictionary<string, string?>? additionalMetadata = null,
        CancellationToken cancellationToken = default)
    {
        var exportName = $"student-diagnostics-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip";
        var exportPath = Path.Combine(AppPaths.GetExportsPath(), exportName);

        if (File.Exists(exportPath))
        {
            File.Delete(exportPath);
        }

        var skippedFiles = new List<string>();
        using (var archive = ZipFile.Open(exportPath, ZipArchiveMode.Create))
        {
            var basePath = AppPaths.GetBasePath();
            AddDirectoryIfExists(archive, Path.Combine(basePath, "logs"), "logs", skippedFiles, cancellationToken);
            AddDirectoryIfExists(archive, Path.Combine(basePath, "Datasets", "dataset"), "dataset", skippedFiles, cancellationToken);
            AddRuntimeMetadata(archive, basePath, skippedFiles, additionalMetadata);
        }

        return exportPath;
    }

    private static void AddDirectoryIfExists(
        ZipArchive archive,
        string sourceDir,
        string entryPrefix,
        List<string> skippedFiles,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = Path.GetRelativePath(sourceDir, file);
            var normalized = relative.Replace('\\', '/');
            AddFileIfReadable(archive, file, $"{entryPrefix}/{normalized}", skippedFiles);
        }
    }

    private static void AddFileIfReadable(ZipArchive archive, string filePath, string entryName, List<string> skippedFiles)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists)
            {
                return;
            }

            if (info.Length > MaxSingleFileBytes)
            {
                skippedFiles.Add($"{entryName} ({info.Length} bytes; over limit)");
                return;
            }

            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
            entry.LastWriteTime = info.LastWriteTimeUtc;

            using var input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var output = entry.Open();
            input.CopyTo(output);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            skippedFiles.Add($"{entryName} ({ex.GetType().Name})");
        }
    }

    private static void AddRuntimeMetadata(
        ZipArchive archive,
        string basePath,
        IReadOnlyList<string> skippedFiles,
        IReadOnlyDictionary<string, string?>? additionalMetadata)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["generatedAtUtc"] = DateTimeOffset.UtcNow,
            ["version"] = ControleduVersion.GetDisplayVersion(),
            ["machineName"] = Environment.MachineName,
            ["userName"] = Environment.UserName,
            ["osDescription"] = RuntimeInformation.OSDescription,
            ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["basePath"] = basePath,
            ["machineBasePath"] = AppPaths.GetMachineBasePath(),
            ["userBasePath"] = AppPaths.GetUserBasePath(),
            ["fileLoggingEnabled"] = AppPaths.IsFileLoggingEnabled(),
            ["entryAssembly"] = Assembly.GetEntryAssembly()?.GetName().Name,
            ["skippedFiles"] = skippedFiles,
        };

        if (additionalMetadata is not null)
        {
            foreach (var item in additionalMetadata)
            {
                metadata[item.Key] = item.Value;
            }
        }

        var payload = JsonSerializer.Serialize(metadata, JsonOptions);
        var entry = archive.CreateEntry("runtime/metadata.json", CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(payload);
    }
}
