namespace Controledu.Transport.Dto;

/// <summary>
/// File upload initialization response.
/// </summary>
public sealed record FileUploadInitResponse(
    string TransferId,
    int TotalChunks,
    DateTimeOffset CreatedAtUtc);

