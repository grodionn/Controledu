using Controledu.Transport.Dto;
using System.Collections.Concurrent;

namespace Controledu.Teacher.Server.Services;

/// <summary>
/// Anti-spam gate for repeated student signals.
/// </summary>
public interface IStudentSignalGate
{
    /// <summary>
    /// Returns true when signal can be forwarded to teacher UI.
    /// </summary>
    bool ShouldForward(string studentId, StudentSignalType signalType, DateTimeOffset nowUtc, TimeSpan cooldown);
}

internal sealed class StudentSignalGate : IStudentSignalGate
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastForwardedUtc = new(StringComparer.Ordinal);

    public bool ShouldForward(string studentId, StudentSignalType signalType, DateTimeOffset nowUtc, TimeSpan cooldown)
    {
        if (string.IsNullOrWhiteSpace(studentId) || signalType == StudentSignalType.None)
        {
            return false;
        }

        var normalizedCooldown = cooldown <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : cooldown;
        var key = $"{studentId}:{signalType}";

        while (true)
        {
            if (!_lastForwardedUtc.TryGetValue(key, out var previousUtc))
            {
                if (_lastForwardedUtc.TryAdd(key, nowUtc))
                {
                    return true;
                }

                continue;
            }

            if (nowUtc - previousUtc < normalizedCooldown)
            {
                return false;
            }

            if (_lastForwardedUtc.TryUpdate(key, nowUtc, previousUtc))
            {
                return true;
            }
        }
    }
}

