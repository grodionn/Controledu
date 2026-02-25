using Controledu.Transport.Dto;
using Controledu.Teacher.Server.Options;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Globalization;

namespace Controledu.Teacher.Server.Services;

/// <summary>
/// In-memory one-time PIN manager for student pairing.
/// </summary>
public sealed class PairingCodeService(IOptions<TeacherServerOptions> options, ISystemClock clock) : IPairingCodeService
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _activePins = new();

    /// <inheritdoc />
    public PairingPinDto Generate()
    {
        CleanupExpiredPins();
        var pin = Random.Shared.Next(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var expires = clock.UtcNow.AddSeconds(Math.Max(1, options.Value.PairingPinLifetimeSeconds));
        _activePins[pin] = expires;
        return new PairingPinDto(pin, expires);
    }

    /// <inheritdoc />
    public bool TryConsume(string pinCode)
    {
        CleanupExpiredPins();

        if (string.IsNullOrWhiteSpace(pinCode) || !_activePins.TryGetValue(pinCode, out var expiresAt))
        {
            return false;
        }

        if (expiresAt <= clock.UtcNow)
        {
            _activePins.TryRemove(pinCode, out _);
            return false;
        }

        return _activePins.TryRemove(pinCode, out _);
    }

    /// <inheritdoc />
    public bool IsValid(string pinCode)
    {
        CleanupExpiredPins();

        return !string.IsNullOrWhiteSpace(pinCode)
            && _activePins.TryGetValue(pinCode, out var expiresAt)
            && expiresAt > clock.UtcNow;
    }

    private void CleanupExpiredPins()
    {
        foreach (var pin in _activePins)
        {
            if (pin.Value <= clock.UtcNow)
            {
                _activePins.TryRemove(pin.Key, out _);
            }
        }
    }
}


