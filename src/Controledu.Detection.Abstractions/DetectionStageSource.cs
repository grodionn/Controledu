namespace Controledu.Detection.Abstractions;

/// <summary>
/// Source stage of a detection result.
/// </summary>
public enum DetectionStageSource
{
    None = 0,
    MetadataRule = 1,
    OnnxBinary = 2,
    OnnxMulticlass = 3,
    Fused = 4,
}
