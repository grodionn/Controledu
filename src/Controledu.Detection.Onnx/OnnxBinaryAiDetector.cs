using Controledu.Detection.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Controledu.Detection.Onnx;

/// <summary>
/// ONNX binary detector (AI_UI vs NOT_AI_UI).
/// </summary>
public sealed class OnnxBinaryAiDetector : IAiUiDetector, IDisposable
{
    private readonly ILogger<OnnxBinaryAiDetector> _logger;
    private readonly OnnxModelConfig _config;
    private InferenceSession? _session;
    private bool _isDisabled;

    /// <inheritdoc />
    public string Name => nameof(OnnxBinaryAiDetector);

    /// <summary>
    /// Creates binary ONNX detector.
    /// </summary>
    public OnnxBinaryAiDetector(IOptions<OnnxModelConfig> options, ILogger<OnnxBinaryAiDetector> logger)
    {
        _logger = logger;
        _config = options.Value;
        InitializeSession();
    }

    /// <inheritdoc />
    public Task<DetectionResult?> AnalyzeAsync(DetectionObservation observation, DetectionSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (_isDisabled || _session is null || observation.FrameBytes is null || observation.FrameBytes.Length == 0)
        {
            return Task.FromResult<DetectionResult?>(null);
        }

        try
        {
            var inputName = _session.InputMetadata.Keys.First();
            var tensor = OnnxTensorFactory.CreateNormalizedTensor(observation.FrameBytes, _config.InputWidth, _config.InputHeight);
            using var runResults = _session.Run(new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, tensor),
            });

            var first = runResults.First().AsEnumerable<float>().ToArray();
            if (first.Length == 0)
            {
                return Task.FromResult<DetectionResult?>(null);
            }

            double confidence;
            if (first.Length == 1)
            {
                confidence = Sigmoid(first[0]);
            }
            else
            {
                var probabilities = Softmax(first);
                confidence = probabilities.Length > 1 ? probabilities[1] : probabilities[0];
            }

            var isPositive = confidence >= _config.BinaryThreshold;
            var detection = new DetectionResult(
                isPositive,
                Math.Clamp(confidence, 0, 1),
                isPositive ? DetectionClass.UnknownAi : DetectionClass.None,
                DetectionStageSource.OnnxBinary,
                "ONNX binary classifier inference",
                Path.GetFileName(_config.BinaryModelPath),
                null,
                false);

            return Task.FromResult<DetectionResult?>(detection);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ONNX binary inference failed");
            return Task.FromResult<DetectionResult?>(null);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }

    private void InitializeSession()
    {
        if (!_config.EnableBinary)
        {
            _isDisabled = true;
            return;
        }

        var path = ResolvePath(_config.BinaryModelPath);
        if (!File.Exists(path))
        {
            _isDisabled = true;
            _logger.LogWarning("ONNX binary model not found at {ModelPath}. Detector is disabled.", path);
            return;
        }

        try
        {
            _session = new InferenceSession(path);
        }
        catch (Exception ex)
        {
            _isDisabled = true;
            _logger.LogWarning(ex, "Failed to initialize ONNX binary session for {ModelPath}", path);
        }
    }

    private static string ResolvePath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
    }

    private static double Sigmoid(float value) => 1d / (1d + Math.Exp(-value));

    private static double[] Softmax(IReadOnlyList<float> values)
    {
        var max = values.Max();
        var exp = values.Select(value => Math.Exp(value - max)).ToArray();
        var sum = exp.Sum();
        if (sum <= double.Epsilon)
        {
            return values.Select(_ => 0d).ToArray();
        }

        return exp.Select(value => value / sum).ToArray();
    }
}


