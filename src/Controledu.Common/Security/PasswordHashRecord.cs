namespace Controledu.Common.Security;

/// <summary>
/// Serialized PBKDF2 password hash payload.
/// </summary>
public sealed record PasswordHashRecord(
    string Algorithm,
    int Iterations,
    int SaltSize,
    int KeySize,
    string SaltBase64,
    string HashBase64);
