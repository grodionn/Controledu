namespace Controledu.Transport.Dto;

/// <summary>
/// Command sent to student agent to start file download.
/// </summary>
public sealed record FileTransferCommandDto(
    string TransferId,
    string FileName,
    long FileSize,
    string Sha256,
    int ChunkSize,
    int TotalChunks,
    DateTimeOffset CreatedAtUtc);

