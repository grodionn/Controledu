using Controledu.Common.Runtime;
using Controledu.Student.Host.Options;
using Controledu.Storage.Stores;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Controledu.Student.Host.Services;

/// <summary>
/// Handles local hand-raise requests and cooldown for endpoint overlay.
/// </summary>
public interface IHandRaiseRequestService
{
    /// <summary>
    /// Queues hand-raise request for Student.Agent.
    /// </summary>
    Task<HandRaiseRequestResult> RequestAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns remaining cooldown before next request can be sent.
    /// </summary>
    TimeSpan GetRemainingCooldown();
}

/// <summary>
/// Hand-raise request result.
/// </summary>
public sealed record HandRaiseRequestResult(bool Accepted, TimeSpan RetryAfter);

internal sealed class HandRaiseRequestService(
    ISettingsStore settingsStore,
    IOptions<StudentHostOptions> options) : IHandRaiseRequestService
{
    private readonly object _sync = new();
    private DateTimeOffset _nextAllowedAtUtc = DateTimeOffset.MinValue;

    public async Task<HandRaiseRequestResult> RequestAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var cooldown = TimeSpan.FromSeconds(Math.Max(5, options.Value.HandRaiseCooldownSeconds));

        lock (_sync)
        {
            if (nowUtc < _nextAllowedAtUtc)
            {
                return new HandRaiseRequestResult(false, _nextAllowedAtUtc - nowUtc);
            }

            _nextAllowedAtUtc = nowUtc.Add(cooldown);
        }

        await settingsStore.SetAsync(DetectionSettingKeys.HandRaiseRequestedAtUtc, nowUtc.ToString("O", CultureInfo.InvariantCulture), cancellationToken);
        return new HandRaiseRequestResult(true, TimeSpan.Zero);
    }

    public TimeSpan GetRemainingCooldown()
    {
        var nowUtc = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            if (nowUtc >= _nextAllowedAtUtc)
            {
                return TimeSpan.Zero;
            }

            return _nextAllowedAtUtc - nowUtc;
        }
    }
}

