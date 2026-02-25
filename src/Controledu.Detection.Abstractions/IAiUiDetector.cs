namespace Controledu.Detection.Abstractions;

/// <summary>
/// AI UI detector abstraction.
/// </summary>
public interface IAiUiDetector
{
    /// <summary>
    /// Detector display name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Performs detection and returns optional result.
    /// </summary>
    Task<DetectionResult?> AnalyzeAsync(DetectionObservation observation, DetectionSettings settings, CancellationToken cancellationToken = default);
}
