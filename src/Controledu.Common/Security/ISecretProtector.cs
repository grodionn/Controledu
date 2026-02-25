namespace Controledu.Common.Security;

/// <summary>
/// Abstraction for platform-specific secret protection.
/// </summary>
public interface ISecretProtector
{
    /// <summary>
    /// Gets the current protector implementation name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Protects plaintext bytes.
    /// </summary>
    byte[] Protect(byte[] plaintext);

    /// <summary>
    /// Restores protected bytes.
    /// </summary>
    byte[] Unprotect(byte[] protectedData);
}
