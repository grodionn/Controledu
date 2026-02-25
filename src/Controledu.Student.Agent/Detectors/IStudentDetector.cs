using Controledu.Common.Models;

namespace Controledu.Student.Agent.Detectors;

/// <summary>
/// Student-side detector interface for policy violations.
/// </summary>
public interface IStudentDetector
{
    /// <summary>
    /// Detector display name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Analyzes observation and returns optional alert result.
    /// </summary>
    Task<DetectionResult?> AnalyzeAsync(Observation observation, CancellationToken cancellationToken = default);
}
