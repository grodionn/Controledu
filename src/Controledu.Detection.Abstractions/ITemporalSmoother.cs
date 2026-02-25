namespace Controledu.Detection.Abstractions;

/// <summary>
/// Temporal smoothing abstraction.
/// </summary>
public interface ITemporalSmoother
{
    /// <summary>
    /// Applies temporal smoothing to a raw result.
    /// </summary>
    TemporalSmoothingResult Apply(DetectionResult result, DetectionSettings settings, DateTimeOffset nowUtc);
}
