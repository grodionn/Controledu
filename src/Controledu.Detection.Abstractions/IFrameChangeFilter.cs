namespace Controledu.Detection.Abstractions;

/// <summary>
/// Frame-change pre-filter abstraction.
/// </summary>
public interface IFrameChangeFilter
{
    /// <summary>
    /// Evaluates frame-change state and whether re-analysis is required.
    /// </summary>
    FrameChangeFilterResult Evaluate(DetectionObservation observation, DetectionSettings settings);
}
