namespace Controledu.Transport.Dto;

/// <summary>
/// File upload initialization request.
/// </summary>
public sealed record FileUploadInitRequest(
    string FileName,
    long FileSize,
    string Sha256,
    int ChunkSize,
    string UploadedBy);

