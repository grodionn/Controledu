using Controledu.Common.IO;
using Controledu.Transport.Dto;
using Controledu.Teacher.Server.Models;
using Controledu.Teacher.Server.Options;
using Microsoft.Extensions.Options;

namespace Controledu.Teacher.Server.Services;

/// <summary>
/// Manages teacher-side uploaded file transfers.
/// </summary>
public interface IFileTransferCoordinator
{
    /// <summary>
    /// Initializes new transfer upload session.
    /// </summary>
    Task<FileUploadInitResponse> InitializeUploadAsync(FileUploadInitRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores one chunk for transfer.
    /// </summary>
    Task<FileChunkUploadResult> SaveChunkAsync(string transferId, int chunkIndex, byte[] chunkData, string? expectedChunkSha256, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates dispatch command for uploaded transfer.
    /// </summary>
    Task<FileTransferCommandDto> CreateDispatchCommandAsync(string transferId, IReadOnlyList<string> targetClientIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes missing chunks against student state.
    /// </summary>
    Task<MissingChunksResponseDto> GetMissingChunksAsync(string transferId, IReadOnlyList<int> existingChunkIndexes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets chunk payload for download.
    /// </summary>
    Task<FileChunkDto> GetChunkAsync(string transferId, int chunkIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracks student progress.
    /// </summary>
    void UpdateProgress(FileDeliveryProgressDto progress);
}

internal sealed class FileTransferCoordinator(IOptions<TeacherServerOptions> options) : IFileTransferCoordinator, IDisposable
{
    private readonly Dictionary<string, FileTransferSession> _transfers = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private bool _disposed;

    private string ResolveTransferRoot()
    {
        var configured = options.Value.TransferRoot;
        if (Path.IsPathRooted(configured))
        {
            Directory.CreateDirectory(configured);
            return configured;
        }

        var root = Path.Combine(Controledu.Common.Runtime.AppPaths.GetBasePath(), configured);
        Directory.CreateDirectory(root);
        return root;
    }

    public Task<FileUploadInitResponse> InitializeUploadAsync(FileUploadInitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var transferId = Guid.NewGuid().ToString("N");
        var totalChunks = ChunkingUtility.GetChunkCount(request.FileSize, request.ChunkSize);
        var createdAt = DateTimeOffset.UtcNow;
        var transferDirectory = Path.Combine(ResolveTransferRoot(), transferId);
        Directory.CreateDirectory(transferDirectory);

        var session = new FileTransferSession
        {
            TransferId = transferId,
            FileName = request.FileName,
            Sha256 = request.Sha256,
            FileSize = request.FileSize,
            ChunkSize = request.ChunkSize,
            TotalChunks = totalChunks,
            TransferDirectory = transferDirectory,
            CreatedAtUtc = createdAt,
            UploadedBy = request.UploadedBy,
        };

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _transfers[transferId] = session;
        }

        return Task.FromResult(new FileUploadInitResponse(transferId, totalChunks, createdAt));
    }

    public async Task<FileChunkUploadResult> SaveChunkAsync(string transferId, int chunkIndex, byte[] chunkData, string? expectedChunkSha256, CancellationToken cancellationToken = default)
    {
        var session = GetSessionOrThrow(transferId);

        if (chunkIndex < 0 || chunkIndex >= session.TotalChunks)
        {
            return new FileChunkUploadResult(false, chunkIndex, "Chunk index out of range.");
        }

        if (!string.IsNullOrWhiteSpace(expectedChunkSha256) && !HashingUtility.VerifySha256(chunkData, expectedChunkSha256))
        {
            return new FileChunkUploadResult(false, chunkIndex, "Chunk hash mismatch.");
        }

        await session.Lock.WaitAsync(cancellationToken);
        try
        {
            var chunkPath = GetChunkPath(session, chunkIndex);
            await File.WriteAllBytesAsync(chunkPath, chunkData, cancellationToken);
            session.UploadedChunks.Add(chunkIndex);
            return new FileChunkUploadResult(true, chunkIndex, "Chunk stored.");
        }
        finally
        {
            session.Lock.Release();
        }
    }

    public async Task<FileTransferCommandDto> CreateDispatchCommandAsync(string transferId, IReadOnlyList<string> targetClientIds, CancellationToken cancellationToken = default)
    {
        var session = GetSessionOrThrow(transferId);
        await session.Lock.WaitAsync(cancellationToken);
        try
        {
            if (session.UploadedChunks.Count < session.TotalChunks)
            {
                throw new InvalidOperationException("File upload is incomplete.");
            }

            foreach (var targetClientId in targetClientIds.Where(static x => !string.IsNullOrWhiteSpace(x)))
            {
                session.TargetClientIds.Add(targetClientId);
            }

            return new FileTransferCommandDto(
                session.TransferId,
                session.FileName,
                session.FileSize,
                session.Sha256,
                session.ChunkSize,
                session.TotalChunks,
                session.CreatedAtUtc);
        }
        finally
        {
            session.Lock.Release();
        }
    }

    public async Task<MissingChunksResponseDto> GetMissingChunksAsync(string transferId, IReadOnlyList<int> existingChunkIndexes, CancellationToken cancellationToken = default)
    {
        var session = GetSessionOrThrow(transferId);
        await session.Lock.WaitAsync(cancellationToken);
        try
        {
            var available = new HashSet<int>(session.UploadedChunks);
            var missing = ChunkingUtility
                .GetMissingChunks(session.TotalChunks, existingChunkIndexes)
                .Where(available.Contains)
                .ToArray();

            return new MissingChunksResponseDto(missing);
        }
        finally
        {
            session.Lock.Release();
        }
    }

    public async Task<FileChunkDto> GetChunkAsync(string transferId, int chunkIndex, CancellationToken cancellationToken = default)
    {
        var session = GetSessionOrThrow(transferId);
        await session.Lock.WaitAsync(cancellationToken);
        try
        {
            if (!session.UploadedChunks.Contains(chunkIndex))
            {
                throw new FileNotFoundException("Chunk not uploaded.", chunkIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            var chunkPath = GetChunkPath(session, chunkIndex);
            var data = await File.ReadAllBytesAsync(chunkPath, cancellationToken);
            return new FileChunkDto(transferId, chunkIndex, data, HashingUtility.Sha256Hex(data));
        }
        finally
        {
            session.Lock.Release();
        }
    }

    public void UpdateProgress(FileDeliveryProgressDto progress)
    {
        var session = TryGetSession(progress.TransferId);
        if (session is null)
        {
            return;
        }

        session.Lock.Wait();
        try
        {
            session.ProgressByClient[progress.ClientId] = progress;
        }
        finally
        {
            session.Lock.Release();
        }
    }

    private FileTransferSession GetSessionOrThrow(string transferId)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_transfers.TryGetValue(transferId, out var session))
            {
                throw new InvalidOperationException($"Transfer {transferId} was not found.");
            }

            return session;
        }
    }

    private FileTransferSession? TryGetSession(string transferId)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return null;
            }

            return _transfers.GetValueOrDefault(transferId);
        }
    }

    private static string GetChunkPath(FileTransferSession session, int chunkIndex) =>
        Path.Combine(session.TransferDirectory, $"{chunkIndex:D8}.chunk");

    public void Dispose()
    {
        FileTransferSession[] sessions;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            sessions = _transfers.Values.ToArray();
            _transfers.Clear();
        }

        foreach (var session in sessions)
        {
            session.Lock.Dispose();
        }
    }
}

