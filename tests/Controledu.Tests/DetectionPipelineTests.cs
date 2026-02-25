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
}
