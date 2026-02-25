using Controledu.Detection.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;

namespace Controledu.Detection.Onnx;

/// <summary>
/// ONNX multiclass detector for AI interface family prediction.
/// </summary>
public sealed class OnnxMulticlassAiDetector : IAiUiDetector, IDisposable
{
    private readonly ILogger<OnnxMulticlassAiDetector> _logger;
    private readonly OnnxModelConfig _config;
    private readonly string[] _labels;
    private InferenceSession? _session;
    private bool _isDisabled;

    /// <inheritdoc />
    public string Name => nameof(OnnxMulticlassAiDetector);

    /// <summary>
    /// Creates multiclass ONNX detector.
    /// </summary>
    public OnnxMulticlassAiDetector(IOptions<OnnxModelConfig> options, ILogger<OnnxMulticlassAiDetector> logger)
    {
        _logger = logger;
        _config = options.Value;
        _labels = LoadLabels(_config.ClassLabelsPath);
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

            using var results = _session.Run(new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, tensor),
            });

            var logits = results.First().AsEnumerable<float>().ToArray();
            if (logits.Length == 0)
            {
                return Task.FromResult<DetectionResult?>(null);
            }

            var probabilities = Softmax(logits);
            var bestIndex = Array.IndexOf(probabilities, probabilities.Max());
            var confidence = probabilities[bestIndex];
            var label = bestIndex >= 0 && bestIndex < _labels.Length ? _labels[bestIndex] : "unknown_ai";
            var mappedClass = MapLabel(label);
            var isPositive = mappedClass != DetectionClass.None && confidence >= _config.MulticlassThreshold;

            var result = new DetectionResult(
                isPositive,
                Math.Clamp(confidence, 0, 1),
                isPositive ? mappedClass : DetectionClass.None,
                DetectionStageSource.OnnxMulticlass,
                $"ONNX multiclass label: {label}",
                Path.GetFileName(_config.MulticlassModelPath),
                [label],
                false);

            return Task.FromResult<DetectionResult?>(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ONNX multiclass inference failed");
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
        if (!_config.EnableMulticlass)
        {
            _isDisabled = true;
            return;
        }

        var path = ResolvePath(_config.MulticlassModelPath);
        if (!File.Exists(path))
        {
            _isDisabled = true;
            _logger.LogWarning("ONNX multiclass model not found at {ModelPath}. Detector is disabled.", path);
            return;
        }

        try
        {
            _session = new InferenceSession(path);
        }
        catch (Exception ex)
        {
            _isDisabled = true;
            _logger.LogWarning(ex, "Failed to initialize ONNX multiclass session for {ModelPath}", path);
        }
    }

    private static string[] LoadLabels(string? labelsPath)
    {
        if (string.IsNullOrWhiteSpace(labelsPath))
        {
            return DefaultLabels();
        }

        try
        {
            var resolved = ResolvePath(labelsPath);
            if (!File.Exists(resolved))
            {
                return DefaultLabels();
            }

            var labels = File.ReadAllLines(resolved)
                .Select(static line => line.Trim())
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            return labels.Length == 0 ? DefaultLabels() : labels;
        }
        catch
        {
            return DefaultLabels();
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

    private static string[] DefaultLabels() =>
    [
        "not_ai_ui",
        "chatgpt_ui",
        "claude_ui",
        "gemini_ui",
        "copilot_ui",
        "perplexity_ui",
        "deepseek_ui",
        "poe_ui",
        "grok_ui",
        "qwen_ui",
        "mistral_ui",
        "meta_ai_ui",
    ];

    private static DetectionClass MapLabel(string label)
    {
        var normalized = label.Trim().ToLowerInvariant();
        return normalized switch
        {
            "chatgpt" or "chatgpt_ui" or "openai" => DetectionClass.ChatGpt,
            "claude" or "claude_ui" => DetectionClass.Claude,
            "gemini" or "gemini_ui" or "bard" => DetectionClass.Gemini,
            "copilot" or "copilot_ui" => DetectionClass.Copilot,
            "perplexity" or "perplexity_ui" => DetectionClass.Perplexity,
            "deepseek" or "deepseek_ui" => DetectionClass.DeepSeek,
            "poe" or "poe_ui" => DetectionClass.Poe,
            "grok" or "grok_ui" => DetectionClass.Grok,
            "qwen" or "qwen_ui" => DetectionClass.Qwen,
            "mistral" or "mistral_ui" => DetectionClass.Mistral,
            "meta ai" or "meta.ai" or "meta_ai" or "meta_ai_ui" => DetectionClass.MetaAi,
            "none" or "not_ai" or "not_ai_ui" => DetectionClass.None,
            _ => DetectionClass.UnknownAi,
        };
    }

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


