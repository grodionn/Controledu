using Controledu.Detection.Abstractions;
using System.Collections.Generic;

namespace Controledu.Detection.Core;

/// <summary>
/// Temporal voting smoother with duplicate-alert cooldown.
/// </summary>
public sealed class TemporalVotingSmoother : ITemporalSmoother
{
    private readonly object _sync = new();
    private readonly Queue<DetectionResult> _window = new();
    private DetectionClass _lastAlertClass = DetectionClass.None;
    private DateTimeOffset _lastAlertAtUtc = DateTimeOffset.MinValue;

    /// <inheritdoc />
    public TemporalSmoothingResult Apply(DetectionResult result, DetectionSettings settings, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(settings);

        lock (_sync)
        {
            var windowSize = Math.Max(1, settings.TemporalWindowSize);
            while (_window.Count >= windowSize)
            {
                _ = _window.Dequeue();
            }

            _window.Enqueue(result);

            var positives = _window.Where(static x => x.IsAiUiDetected).ToArray();
            var stablePositive = positives.Length >= Math.Max(1, settings.TemporalRequiredVotes);

            if (!stablePositive)
            {
                return new TemporalSmoothingResult(result with { IsStable = false }, false);
            }

            var stableClass = positives
                .GroupBy(static x => x.Class)
                .OrderByDescending(static group => group.Count())
                .ThenByDescending(static group => group.Max(x => x.Confidence))
                .Select(static group => group.Key)
                .FirstOrDefault();

            if (stableClass == DetectionClass.None)
            {
                stableClass = positives[^1].Class;
            }

            var stableConfidence = positives.Average(static x => x.Confidence);
            var cooldown = TimeSpan.FromSeconds(Math.Max(1, settings.CooldownSeconds));
            var inCooldown = stableClass == _lastAlertClass && nowUtc - _lastAlertAtUtc < cooldown;

            var stableResult = result with
            {
                IsAiUiDetected = true,
                IsStable = true,
                Class = stableClass,
                Confidence = Math.Clamp(stableConfidence, 0, 1),
            };

            if (inCooldown)
            {
                return new TemporalSmoothingResult(stableResult, false);
            }

            _lastAlertClass = stableClass;
            _lastAlertAtUtc = nowUtc;
            return new TemporalSmoothingResult(stableResult, true);
        }
    }
}
