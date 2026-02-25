namespace Controledu.Transport.Dto;

/// <summary>
/// Encoded screen frame from student.
/// </summary>
public sealed record ScreenFrameDto(
    string ClientId,
    byte[] Payload,
    string ImageFormat,
    int Width,
    int Height,
    int Sequence,
    DateTimeOffset CapturedAtUtc);

