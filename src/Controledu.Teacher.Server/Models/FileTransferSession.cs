using Controledu.Transport.Dto;

namespace Controledu.Teacher.Server.Models;

internal sealed class FileTransferSession
{
    public required string TransferId { get; init; }

    public required string FileName { get; init; }

    public required string Sha256 { get; init; }

    public required long FileSize { get; init; }

    public required int ChunkSize { get; init; }

    public required int TotalChunks { get; init; }

    public required string TransferDirectory { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required string UploadedBy { get; init; }

    public HashSet<int> UploadedChunks { get; } = [];

    public Dictionary<string, FileDeliveryProgressDto> ProgressByClient { get; } = [];

    public HashSet<string> TargetClientIds { get; } = [];

    public SemaphoreSlim Lock { get; } = new(1, 1);
}

