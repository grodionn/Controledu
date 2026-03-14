namespace Controledu.Detection.Onnx;

/// <summary>
/// ONNX runtime configuration for AI UI detection models.
/// </summary>
public sealed class OnnxModelConfig
{
    /// <summary>
    /// Enables binary model inference.
    /// </summary>
    public bool EnableBinary { get; set; }

    /// <summary>
    /// Enables multiclass model inference.
    /// </summary>
    public bool EnableMulticlass { get; set; }

    /// <summary>
    /// Relative or absolute path to binary ONNX model.
    /// </summary>
    public string BinaryModelPath { get; set; } = "models/ai-ui-binary.onnx";

    /// <summary>
    /// Relative or absolute path to multiclass ONNX model.
    /// </summary>
    public string MulticlassModelPath { get; set; } = "models/ai-ui-multiclass.onnx";

    /// <summary>
    /// Optional labels file path for multiclass output.
    /// </summary>
    public string? ClassLabelsPath { get; set; } = "models/labels.txt";

    /// <summary>
    /// Input tensor width.
    /// </summary>
    public int InputWidth { get; set; } = 224;

    /// <summary>
    /// Input tensor height.
    /// </summary>
    public int InputHeight { get; set; } = 224;

}
