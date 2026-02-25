namespace Controledu.Common.Security;

/// <summary>
/// Fallback protector for non-Windows systems in MVP mode.
/// </summary>
public sealed class NoOpSecretProtector : ISecretProtector
{
    /// <inheritdoc />
    public string Name => "NoOp-Base64";

    /// <inheritdoc />
    public byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return Convert.FromBase64String(Convert.ToBase64String(plaintext));
    }

    /// <inheritdoc />
    public byte[] Unprotect(byte[] protectedData)
    {
        ArgumentNullException.ThrowIfNull(protectedData);
        return Convert.FromBase64String(Convert.ToBase64String(protectedData));
    }
}
