using Controledu.Common.IO;
using Controledu.Common.Runtime;
using Controledu.Transport.Dto;
using Controledu.Storage.Models;
using Controledu.Storage.Stores;
using Controledu.Student.Agent.Models;
using System.Net.Http.Json;

namespace Controledu.Student.Agent.Services;

/// <summary>
/// Downloads assigned file transfers with chunk resume support.
/// </summary>
public sealed class FileTransferReceiver(
    ITransferStateStore transferStateStore,
    StudentHubClient hubClient,
    ILogger<FileTransferReceiver> logger)
{
    /// <summary>
    /// Processes one file transfer command end-to-end.
    /// </summary>
    public async Task ProcessAsync(ResolvedStudentBinding binding, FileTransferCommandDto command, CancellationToken cancellationToken)
    {
        var transferRoot = Path.Combine(AppPaths.GetBasePath(), "student-transfers");
        Directory.CreateDirectory(transferRoot);

        var state = await transferStateStore.GetAsync(command.TransferId, cancellationToken);
        var partialPath = state?.PartialFilePath ?? Path.Combine(transferRoot, $"{command.TransferId}.partial");

        var completedChunks = state?.CompletedChunkIndexes.ToHashSet() ?? [];

        EnsurePartialFile(partialPath, command.FileSize);

        using var http = new HttpClient
        {
            BaseAddress = new Uri(binding.ServerBaseUrl.TrimEnd('/') + "/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(30),
        };
        http.DefaultRequestHeaders.Add("X-Controledu-Token", binding.Token);

        var missingResponse = await RequestMissingChunksAsync(http, command.TransferId, binding.ClientId, completedChunks, cancellationToken);

        foreach (var chunkIndex in missingResponse.MissingChunkIndexes.OrderBy(static x => x))
        {
            var chunkResponse = await http.GetAsync($"api/files/{command.TransferId}/chunk/{chunkIndex}?clientId={Uri.EscapeDataString(binding.ClientId)}", cancellationToken);
            chunkResponse.EnsureSuccessStatusCode();

            var data = await chunkResponse.Content.ReadAsByteArrayAsync(cancellationToken);
            var expectedChunkHash = chunkResponse.Headers.TryGetValues("X-Chunk-Sha256", out var values)
                ? values.FirstOrDefault()
                : null;

            if (!string.IsNullOrWhiteSpace(expectedChunkHash) && !HashingUtility.VerifySha256(data, expectedChunkHash))
            {
                throw new InvalidOperationException($"Chunk hash mismatch for chunk {chunkIndex}.");
            }

            await WriteChunkAsync(partialPath, chunkIndex, command.ChunkSize, data, cancellationToken);
            completedChunks.Add(chunkIndex);

            var updatedState = new TransferStateModel(
                command.TransferId,
                command.FileName,
                command.Sha256,
                command.ChunkSize,
                command.TotalChunks,
                completedChunks,
                partialPath,
                DateTimeOffset.UtcNow);

            await transferStateStore.SaveAsync(updatedState, cancellationToken);

            await hubClient.SendFileProgressAsync(
                new FileDeliveryProgressDto(command.TransferId, binding.ClientId, completedChunks.Count, command.TotalChunks, false, null),
                cancellationToken);
        }

        await VerifyAndFinalizeAsync(binding, command, partialPath, completedChunks.Count, cancellationToken);
        await transferStateStore.DeleteAsync(command.TransferId, cancellationToken);

        try
        {
            File.Delete(partialPath);
        }
        catch
        {
            // ignored
        }
    }

    private static void EnsurePartialFile(string partialPath, long fileSize)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);

        using var stream = new FileStream(partialPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        if (stream.Length != fileSize)
        {
            stream.SetLength(fileSize);
        }
    }

    private static async Task WriteChunkAsync(string partialPath, int chunkIndex, int chunkSize, byte[] data, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(partialPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);
        stream.Seek((long)chunkIndex * chunkSize, SeekOrigin.Begin);
        await stream.WriteAsync(data, cancellationToken);
    }

    private static async Task<MissingChunksResponseDto> RequestMissingChunksAsync(
        HttpClient http,
        string transferId,
        string clientId,
        IReadOnlyCollection<int> completedChunks,
        CancellationToken cancellationToken)
    {
        var request = new MissingChunksRequestDto(clientId, completedChunks.OrderBy(static x => x).ToArray());
        var response = await http.PostAsJsonAsync($"api/files/{transferId}/missing", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<MissingChunksResponseDto>(cancellationToken: cancellationToken)
               ?? new MissingChunksResponseDto([]);
    }

    private async Task VerifyAndFinalizeAsync(
        ResolvedStudentBinding binding,
        FileTransferCommandDto command,
        string partialPath,
        int completedChunkCount,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(partialPath);
        var fullHash = HashingUtility.Sha256Hex(stream);

        if (!string.Equals(fullHash, command.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            await hubClient.SendFileProgressAsync(
                new FileDeliveryProgressDto(command.TransferId, binding.ClientId, completedChunkCount, command.TotalChunks, false, "SHA256 mismatch"),
                cancellationToken);

            throw new InvalidOperationException("Downloaded file hash mismatch.");
        }

        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopPath))
        {
            desktopPath = AppPaths.GetBasePath();
        }

        Directory.CreateDirectory(desktopPath);

        var targetPath = Path.Combine(desktopPath, command.FileName);
        if (File.Exists(targetPath))
        {
            var suffix = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            targetPath = Path.Combine(desktopPath, $"{Path.GetFileNameWithoutExtension(command.FileName)}_{suffix}{Path.GetExtension(command.FileName)}");
        }

        File.Copy(partialPath, targetPath, overwrite: false);

        logger.LogInformation("Transfer {TransferId} saved to {Path}", command.TransferId, targetPath);

        await hubClient.SendFileProgressAsync(
            new FileDeliveryProgressDto(command.TransferId, binding.ClientId, command.TotalChunks, command.TotalChunks, true, null),
            cancellationToken);
    }
}

