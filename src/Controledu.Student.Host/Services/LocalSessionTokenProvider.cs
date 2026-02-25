using System.Security.Cryptography;

namespace Controledu.Student.Host.Services;

/// <summary>
/// Provides in-memory local API session token.
/// </summary>
public interface ILocalSessionTokenProvider
{
    /// <summary>
    /// Current token value.
    /// </summary>
    string Token { get; }
}

internal sealed class LocalSessionTokenProvider : ILocalSessionTokenProvider
{
    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
