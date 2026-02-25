namespace Controledu.Student.Agent.Models;

/// <summary>
/// Resolved student binding with decrypted auth token.
/// </summary>
public sealed record ResolvedStudentBinding(
    string ServerId,
    string ServerName,
    string ServerBaseUrl,
    string ServerFingerprint,
    string ClientId,
    string Token);

/// <summary>
/// Encoded screenshot capture result.
/// </summary>
public sealed record ScreenCaptureResult(
    byte[] Payload,
    int Width,
    int Height,
    string Format);
