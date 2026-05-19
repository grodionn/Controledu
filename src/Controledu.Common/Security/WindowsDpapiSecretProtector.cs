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
    public string Name => "DPAPI-LocalMachine";

    /// <inheritdoc />
    public byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.LocalMachine);
    }

    /// <inheritdoc />
    public byte[] Unprotect(byte[] protectedData)
    {
        ArgumentNullException.ThrowIfNull(protectedData);
        try
        {
            return ProtectedData.Unprotect(protectedData, optionalEntropy: null, DataProtectionScope.LocalMachine);
        }
        catch (CryptographicException)
        {
            // Migration path for bindings created before 0.1.92 under the interactive user profile.
            return ProtectedData.Unprotect(protectedData, optionalEntropy: null, DataProtectionScope.CurrentUser);
        }
    }
}
