using Controledu.Storage.Stores;
using Controledu.Teacher.Server.Options;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Controledu.Teacher.Server.Services;

/// <summary>
/// Resolves a stable teacher API token from configuration or persisted settings.
/// </summary>
public interface ITeacherApiTokenProvider
{
    /// <summary>
    /// Returns current teacher API token.
    /// </summary>
    Task<string> GetTokenAsync(CancellationToken cancellationToken = default);
}

internal sealed class TeacherApiTokenProvider(
    ISettingsStore settingsStore,
    IOptions<TeacherServerOptions> options) : ITeacherApiTokenProvider
{
    private const string TokenSettingKey = "teacher.auth.api-token";
    private readonly SemaphoreSlim _sync = new(1, 1);
    private string? _cached;

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_cached))
        {
            return _cached!;
        }

        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_cached))
            {
                return _cached!;
            }

            var configured = options.Value.TeacherApiToken?.Trim();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                _cached = configured;
                return _cached;
            }

            var existing = (await settingsStore.GetAsync(TokenSettingKey, cancellationToken))?.Trim();
            if (!string.IsNullOrWhiteSpace(existing))
            {
                _cached = existing;
                return _cached;
            }

            var generated = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await settingsStore.SetAsync(TokenSettingKey, generated, cancellationToken);
            _cached = generated;
            return _cached;
        }
        finally
        {
            _sync.Release();
        }
    }
}
