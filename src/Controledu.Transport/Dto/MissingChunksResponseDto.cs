namespace Controledu.Transport.Dto;

/// <summary>
/// Missing chunk list response.
/// </summary>
public sealed record MissingChunksResponseDto(
    IReadOnlyList<int> MissingChunkIndexes);

