using Controledu.Common.Models;
using Controledu.Student.Agent.Options;
using Microsoft.Extensions.Options;

namespace Controledu.Student.Agent.Detectors;

/// <summary>
/// MVP keyword detector for active window title.
/// </summary>
public sealed class WindowTitleKeywordDetector(IOptions<StudentAgentOptions> options) : IStudentDetector
{
    private readonly string[] _keywords = options.Value.DetectorKeywords;

    /// <inheritdoc />
    public string Name => nameof(WindowTitleKeywordDetector);

    /// <inheritdoc />
    public Task<DetectionResult?> AnalyzeAsync(Observation observation, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(observation.ActiveWindowTitle))
        {
            return Task.FromResult<DetectionResult?>(null);
        }

        var matchedKeyword = _keywords.FirstOrDefault(keyword =>
            observation.ActiveWindowTitle.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        if (matchedKeyword is null)
        {
            return Task.FromResult<DetectionResult?>(null);
        }

        var message = $"Обнаружен ключевой паттерн '{matchedKeyword}' в активном окне";
        var result = new DetectionResult(Name, "Warning", message, DateTimeOffset.UtcNow, observation.ActiveWindowTitle);
        return Task.FromResult<DetectionResult?>(result);
    }
}
