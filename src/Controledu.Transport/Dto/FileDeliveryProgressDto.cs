namespace Controledu.Transport.Dto;

/// <summary>
/// Student-side file transfer progress.
/// </summary>
public sealed record FileDeliveryProgressDto(
    string TransferId,
    string ClientId,
    int CompletedChunks,
    int TotalChunks,
    bool Completed,
    string? Error);

