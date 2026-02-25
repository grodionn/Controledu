using Controledu.Common.Security;
using Controledu.Storage.Stores;

namespace Controledu.Student.Host.Services;

/// <summary>
/// Manages student local admin password storage and verification.
/// </summary>
public interface IAdminPasswordService
{
    /// <summary>
    /// Returns true when admin password is configured.
    /// </summary>
    Task<bool> HasPasswordAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets initial admin password.
    /// </summary>
    Task SetInitialPasswordAsync(string password, string confirmPassword, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies supplied password.
    /// </summary>
    Task<bool> VerifyAsync(string password, CancellationToken cancellationToken = default);
}

internal sealed class AdminPasswordService(ISettingsStore settingsStore) : IAdminPasswordService
{
    private const string PasswordHashSettingKey = "student.admin.password.hash";

    public async Task<bool> HasPasswordAsync(CancellationToken cancellationToken = default) =>
        !string.IsNullOrWhiteSpace(await settingsStore.GetAsync(PasswordHashSettingKey, cancellationToken));

    public async Task SetInitialPasswordAsync(string password, string confirmPassword, CancellationToken cancellationToken = default)
    {
        if (await HasPasswordAsync(cancellationToken))
        {
            throw new InvalidOperationException("Admin password is already configured.");
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            throw new InvalidOperationException("Password must be at least 8 characters.");
        }

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Password confirmation does not match.");
        }

        var hashRecord = PasswordHasher.CreateHash(password);
        var serialized = PasswordHasher.Serialize(hashRecord);
        await settingsStore.SetAsync(PasswordHashSettingKey, serialized, cancellationToken);
    }

    public async Task<bool> VerifyAsync(string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        var serialized = await settingsStore.GetAsync(PasswordHashSettingKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return false;
        }

        var record = PasswordHasher.Deserialize(serialized);
        return PasswordHasher.Verify(password, record);
    }
}
