using Controledu.Detection.Abstractions;
using Controledu.Detection.Core;

namespace Controledu.Tests;

public sealed class WindowMetadataDetectorTests
{
    [Fact]
    public async Task AnalyzeAsync_WithChatGptKeywordInTitle_ReturnsPositive()
    {
        var detector = new WindowMetadataDetector();
        var settings = new DetectionSettings();

        var result = await detector.AnalyzeAsync(
            new DetectionObservation
            {
                StudentId = "student-001",
                ActiveWindowTitle = "ChatGPT - OpenAI",
                ActiveProcessName = "msedge",
            },
            settings);

        Assert.NotNull(result);
        Assert.True(result.IsAiUiDetected);
        Assert.Equal(DetectionClass.ChatGpt, result.Class);
        Assert.Equal(DetectionStageSource.MetadataRule, result.StageSource);
    }

    [Fact]
    public async Task AnalyzeAsync_WithWhitelistMatch_ReturnsNegative()
    {
        var detector = new WindowMetadataDetector();
        var settings = new DetectionSettings
        {
            WhitelistKeywords = ["internal-helpdesk.local"],
        };

        var result = await detector.AnalyzeAsync(
            new DetectionObservation
            {
                StudentId = "student-001",
                ActiveWindowTitle = "ChatGPT style help tool",
                BrowserHintUrl = "https://internal-helpdesk.local/assistant",
            },
            settings);

        Assert.NotNull(result);
        Assert.False(result.IsAiUiDetected);
        Assert.Equal(DetectionClass.None, result.Class);
    }

    [Fact]
    public async Task AnalyzeAsync_WithNullMetadata_ReturnsNullWithoutCrash()
    {
        var detector = new WindowMetadataDetector();
        var settings = new DetectionSettings();

        var result = await detector.AnalyzeAsync(
            new DetectionObservation
            {
                StudentId = "student-001",
            },
            settings);

        Assert.Null(result);
    }
}
