namespace Controledu.Detection.Abstractions;

/// <summary>
/// Frame-change evaluation output.
/// </summary>
public sealed record FrameChangeFilterResult(
    string? ScreenFrameHash,
    bool FrameChanged,
    bool ShouldAnalyze);
