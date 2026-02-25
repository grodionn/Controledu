namespace Controledu.Transport.Dto;

/// <summary>
/// Upload result for chunk operation.
/// </summary>
public sealed record FileChunkUploadResult(
    bool Accepted,
    int ChunkIndex,
    string Message);

