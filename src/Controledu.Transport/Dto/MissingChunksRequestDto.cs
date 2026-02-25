namespace Controledu.Transport.Dto;

/// <summary>
/// Request payload with locally existing chunks.
/// </summary>
public sealed record MissingChunksRequestDto(
    string ClientId,
    IReadOnlyList<int> ExistingChunkIndexes);

