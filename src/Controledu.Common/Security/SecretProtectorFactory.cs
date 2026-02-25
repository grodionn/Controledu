namespace Controledu.Common.Security;

/// <summary>
/// Factory for selecting platform-specific secret protector.
/// </summary>
public static class SecretProtectorFactory
{
    /// <summary>
    /// Creates a platform-appropriate secret protector.
    /// </summary>
    public static ISecretProtector CreateDefault() =>
        OperatingSystem.IsWindows() ? new WindowsDpapiSecretProtector() : new NoOpSecretProtector();
}
