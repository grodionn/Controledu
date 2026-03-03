using Controledu.Detection.Abstractions;
using Controledu.Detection.Core;
using Controledu.Detection.Onnx;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Drawing;

namespace Controledu.Tests;

public sealed class DetectionPipelineTests
{
    [Fact]
    public async Task AnalyzeAsync_WithMissingOnnxModel_DoesNotThrowAndReturnsResult()
    {
        var pipeline = CreatePipelineWithMissingOnnxModel();
        var settings = new DetectionSettings
        {
            Enabled = true,
            TemporalWindowSize = 1,
            TemporalRequiredVotes = 1,
        };

        var observation = new DetectionObservation
        {
            StudentId = "student-001",
            TimestampUtc = DateTimeOffset.UtcNow,
            FrameBytes = DetectionTestImageFactory.CreateSolidJpeg(Color.DimGray),
        };

        var decision = await pipeline.AnalyzeAsync(observation, settings);

        Assert.NotNull(decision.Result);
        Assert.False(decision.ShouldEmitAlert);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenFrameUnchanged_UsesCachedResult()
    {
        var pipeline = new DetectionPipeline(
            new PerceptualHashChangeFilter(),
            new WindowMetadataDetector(),
            [],
            new TemporalVotingSmoother(),
            NullLogger<DetectionPipeline>.Instance);

        var settings = new DetectionSettings
        {
            Enabled = true,
            MetadataConfidenceThreshold = 0.55,
            TemporalWindowSize = 1,
            TemporalRequiredVotes = 1,
            MinRecheckIntervalSeconds = 300,
        };

        var frame = DetectionTestImageFactory.CreateSolidJpeg(Color.SteelBlue);
        var first = await pipeline.AnalyzeAsync(
            new DetectionObservation
            {
                StudentId = "student-001",
                TimestampUtc = DateTimeOffset.UtcNow,
                ActiveWindowTitle = "ChatGPT - OpenAI",
                ActiveProcessName = "msedge",
                FrameBytes = frame,
            },
            settings);

        var second = await pipeline.AnalyzeAsync(
            new DetectionObservation
            {
                StudentId = "student-001",
                TimestampUtc = DateTimeOffset.UtcNow.AddSeconds(1),
                ActiveWindowTitle = "ChatGPT - OpenAI",
                ActiveProcessName = "msedge",
                FrameBytes = frame,
            },
            settings);

        Assert.False(first.UsedCachedResult);
        Assert.True(second.UsedCachedResult);
        Assert.Contains("reused previous detection", second.Result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_FusedWithMetadataSpecificClass_PrefersSpecificClassOverUnknownAi()
    {
        var pipeline = CreatePipelineWithDetectors(
            new StubDetector(
                "stub-binary",
                new DetectionResult(
                    true,
                    0.96,
                    DetectionClass.UnknownAi,
                    DetectionStageSource.OnnxBinary,
                    "ONNX binary classifier inference",
                    "ai-ui-binary.onnx",
                    null,
                    false)));

        var settings = new DetectionSettings
        {
            Enabled = true,
            MetadataConfidenceThreshold = 0.64,
            MlConfidenceThreshold = 0.72,
            TemporalWindowSize = 1,
            TemporalRequiredVotes = 1,
        };

        var decision = await pipeline.AnalyzeAsync(
            new DetectionObservation
            {
                StudentId = "student-001",
                TimestampUtc = DateTimeOffset.UtcNow,
                ActiveWindowTitle = "DeepSeek Assistant",
                ActiveProcessName = "deepseek-desktop",
            },
            settings);

        Assert.True(decision.Result.IsAiUiDetected);
        Assert.Equal(DetectionClass.DeepSeek, decision.Result.Class);
        Assert.Equal(DetectionStageSource.Fused, decision.Result.StageSource);
        Assert.Contains("class from MetadataRule", decision.Result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_UnknownAiPositiveWithStrongNotAiMulticlass_SuppressesAlert()
    {
        var pipeline = CreatePipelineWithDetectors(
            new StubDetector(
                "stub-binary",
                new DetectionResult(
                    true,
                    0.96,
                    DetectionClass.UnknownAi,
                    DetectionStageSource.OnnxBinary,
                    "ONNX binary classifier inference",
                    "ai-ui-binary.onnx",
                    null,
                    false)),
            new StubDetector(
                "stub-multiclass",
                new DetectionResult(
                    false,
                    0.95,
                    DetectionClass.None,
                    DetectionStageSource.OnnxMulticlass,
                    "ONNX multiclass label: not_ai_ui",
                    "ai-ui-multiclass.onnx",
                    ["not_ai_ui"],
                    false)));

        var settings = new DetectionSettings
        {
            Enabled = true,
            MetadataConfidenceThreshold = 0.64,
            MlConfidenceThreshold = 0.72,
            TemporalWindowSize = 1,
            TemporalRequiredVotes = 1,
        };

        var decision = await pipeline.AnalyzeAsync(
            new DetectionObservation
            {
                StudentId = "student-001",
                TimestampUtc = DateTimeOffset.UtcNow,
            },
            settings);

        Assert.False(decision.Result.IsAiUiDetected);
        Assert.Equal(DetectionClass.None, decision.Result.Class);
        Assert.Equal(DetectionStageSource.OnnxMulticlass, decision.Result.StageSource);
        Assert.Contains("Suppressed by ONNX multiclass", decision.Result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static DetectionPipeline CreatePipelineWithMissingOnnxModel()
    {
        var onnxConfig = new OnnxModelConfig
        {
            EnableBinary = true,
            BinaryModelPath = "models/this-model-does-not-exist.onnx",
        };

        var onnxDetector = new OnnxBinaryAiDetector(
            Options.Create(onnxConfig),
            NullLogger<OnnxBinaryAiDetector>.Instance);

        return new DetectionPipeline(
            new PerceptualHashChangeFilter(),
            new WindowMetadataDetector(),
            [onnxDetector],
            new TemporalVotingSmoother(),
            NullLogger<DetectionPipeline>.Instance);
    }

    private static DetectionPipeline CreatePipelineWithDetectors(params IAiUiDetector[] detectors)
    {
        return new DetectionPipeline(
            new AlwaysAnalyzeFrameChangeFilter(),
            new WindowMetadataDetector(),
            detectors,
            new TemporalVotingSmoother(),
            NullLogger<DetectionPipeline>.Instance);
    }

    private sealed class AlwaysAnalyzeFrameChangeFilter : IFrameChangeFilter
    {
        public FrameChangeFilterResult Evaluate(DetectionObservation observation, DetectionSettings settings) =>
            new("stub-hash", true, true);
    }

    private sealed class StubDetector(string name, DetectionResult? result) : IAiUiDetector
    {
        public string Name { get; } = name;

        public Task<DetectionResult?> AnalyzeAsync(DetectionObservation observation, DetectionSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
