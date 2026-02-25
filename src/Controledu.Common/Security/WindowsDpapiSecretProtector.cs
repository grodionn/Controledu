using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Controledu.Common.Security;

/// <summary>
/// DPAPI-based secret protector for Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiSecretProtector : ISecretProtector
{
    /// <inheritdoc />
    public string Name => "DPAPI-CurrentUser";

    /// <inheritdoc />
    public byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }

    /// <inheritdoc />
    public byte[] Unprotect(byte[] protectedData)
    {
        ArgumentNullException.ThrowIfNull(protectedData);
        return ProtectedData.Unprotect(protectedData, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }
}
