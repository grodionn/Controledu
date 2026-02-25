using System.Security.Cryptography;
using System.Text;

namespace Controledu.Common.IO;

/// <summary>
/// SHA-256 helpers for files and payloads.
/// </summary>
public static class HashingUtility
{
    /// <summary>
    /// Computes SHA-256 hash for a byte array.
    /// </summary>
    public static string Sha256Hex(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Convert.ToHexString(SHA256.HashData(data));
    }

    /// <summary>
    /// Computes SHA-256 hash for a UTF-8 string.
    /// </summary>
    public static string Sha256Hex(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Sha256Hex(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>
    /// Computes SHA-256 hash for a stream.
    /// </summary>
    public static string Sha256Hex(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>
    /// Validates SHA-256 hash string against payload.
    /// </summary>
    public static bool VerifySha256(byte[] data, string expectedHash)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHash);
        var actual = Sha256Hex(data);
        return string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase);
    }
}
