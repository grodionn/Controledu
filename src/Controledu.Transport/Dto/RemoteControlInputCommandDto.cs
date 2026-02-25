namespace Controledu.Transport.Dto;

/// <summary>
/// Server-to-student remote input command for an approved session.
/// Coordinates are normalized to [0..1] relative to the rendered frame.
/// </summary>
public sealed record RemoteControlInputCommandDto(
    string ClientId,
    string SessionId,
    RemoteControlInputKind Kind,
    double X = 0,
    double Y = 0,
    RemoteMouseButton Button = RemoteMouseButton.None,
    int WheelDelta = 0,
    string? Key = null,
    bool Ctrl = false,
    bool Alt = false,
    bool Shift = false);
