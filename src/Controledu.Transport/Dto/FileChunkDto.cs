namespace Controledu.Transport.Dto;

/// <summary>
/// Chunk response for file download.
/// </summary>
public sealed record FileChunkDto(
    string TransferId,
    int ChunkIndex,
    byte[] Data,
    string Sha256);

