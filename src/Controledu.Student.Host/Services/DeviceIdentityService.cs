using Controledu.Storage.Stores;

namespace Controledu.Student.Host.Services;

/// <summary>
/// Manages persisted student device display name.
/// </summary>
public interface IDeviceIdentityService
{
    /// <summary>
    /// Gets configured device name or machine name fallback.
    /// </summary>
    Task<string> GetDisplayNameAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists device display name.
    /// </summary>
    Task SetDisplayNameAsync(string displayName, CancellationToken cancellationToken = default);
}

internal sealed class DeviceIdentityService(ISettingsStore settingsStore) : IDeviceIdentityService
{
    private const string DeviceNameKey = "student.device.display-name";

    public async Task<string> GetDisplayNameAsync(CancellationToken cancellationToken = default)
    {
        var stored = await settingsStore.GetAsync(DeviceNameKey, cancellationToken);
        return string.IsNullOrWhiteSpace(stored) ? Environment.MachineName : stored.Trim();
    }

    public async Task SetDisplayNameAsync(string displayName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new InvalidOperationException("Device name cannot be empty.");
        }

        var normalized = displayName.Trim();
        if (normalized.Length is < 2 or > 64)
        {
            throw new InvalidOperationException("Device name must be between 2 and 64 characters.");
        }

        await settingsStore.SetAsync(DeviceNameKey, normalized, cancellationToken);
    }
}
