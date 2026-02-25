namespace Controledu.Storage.Entities;

/// <summary>
/// Resume state for a file transfer.
/// </summary>
public sealed class TransferStateEntity
{
    /// <summary>
    /// Transfer id.
    /// </summary>
    public required string TransferId { get; set; }

    /// <summary>
    /// Original file name.
    /// </summary>
    public required string FileName { get; set; }

    /// <summary>
    /// Expected file hash.
    /// </summary>
    public required string Sha256 { get; set; }

    /// <summary>
    /// Chunk size.
    /// </summary>
    public int ChunkSize { get; set; }

    /// <summary>
    /// Number of chunks.
    /// </summary>
    public int TotalChunks { get; set; }

    /// <summary>
    /// JSON serialized completed chunk indexes.
    /// </summary>
    public required string CompletedChunkIndexesJson { get; set; }

    /// <summary>
    /// Path to partial file.
    /// </summary>
    public required string PartialFilePath { get; set; }

    /// <summary>
    /// Last update timestamp.
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
