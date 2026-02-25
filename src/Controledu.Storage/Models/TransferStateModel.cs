namespace Controledu.Storage.Models;

/// <summary>
/// Immutable transfer state projection.
/// </summary>
public sealed record TransferStateModel(
    string TransferId,
    string FileName,
    string Sha256,
    int ChunkSize,
    int TotalChunks,
    IReadOnlyCollection<int> CompletedChunkIndexes,
    string PartialFilePath,
    DateTimeOffset UpdatedAtUtc);
