using System.Globalization;
using System.Security.Cryptography;

namespace Controledu.Common.Security;

/// <summary>
/// Provides password hashing and verification helpers.
/// </summary>
public static class PasswordHasher
{
    private const string AlgorithmName = "PBKDF2-SHA256";

    /// <summary>
    /// Creates a password hash record using PBKDF2.
    /// </summary>
    public static PasswordHashRecord CreateHash(string password, int iterations = 120_000, int saltSize = 16, int keySize = 32)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(saltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, keySize);

        return new PasswordHashRecord(
            AlgorithmName,
            iterations,
            saltSize,
            keySize,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    /// <summary>
    /// Verifies a plaintext password against the stored hash record.
    /// </summary>
    public static bool Verify(string password, PasswordHashRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentNullException.ThrowIfNull(record);

        if (!string.Equals(record.Algorithm, AlgorithmName, StringComparison.Ordinal))
        {
            return false;
        }

        var salt = Convert.FromBase64String(record.SaltBase64);
        var expectedHash = Convert.FromBase64String(record.HashBase64);
        var computed = Rfc2898DeriveBytes.Pbkdf2(password, salt, record.Iterations, HashAlgorithmName.SHA256, record.KeySize);
        return CryptographicOperations.FixedTimeEquals(expectedHash, computed);
    }

    /// <summary>
    /// Serializes a hash record into a compact string.
    /// </summary>
    public static string Serialize(PasswordHashRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return string.Join(
            ':',
            record.Algorithm,
            record.Iterations,
            record.SaltSize,
            record.KeySize,
            record.SaltBase64,
            record.HashBase64);
    }

    /// <summary>
    /// Deserializes a compact hash record string.
    /// </summary>
    public static PasswordHashRecord Deserialize(string serialized)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serialized);
        var parts = serialized.Split(':');
        if (parts.Length != 6)
        {
            throw new FormatException("Invalid password hash record format.");
        }

        return new PasswordHashRecord(
            parts[0],
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            int.Parse(parts[2], CultureInfo.InvariantCulture),
            int.Parse(parts[3], CultureInfo.InvariantCulture),
            parts[4],
            parts[5]);
    }
}
