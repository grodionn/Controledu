using Controledu.Detection.Abstractions;
using Controledu.Detection.Core;
using System.Drawing;

namespace Controledu.Tests;

public sealed class PerceptualHashChangeFilterTests
{
    [Fact]
    public void Evaluate_WithSameFrameTwice_ReportsNoChangeOnSecondCheck()
    {
        var filter = new PerceptualHashChangeFilter();
        var settings = new DetectionSettings
        {
            FrameChangeThreshold = 2,
            MinRecheckIntervalSeconds = 120,
        };

        var frame = DetectionTestImageFactory.CreateSolidJpeg(Color.DarkSlateGray);
        var first = filter.Evaluate(CreateObservation(frame, DateTimeOffset.UtcNow), settings);
        var second = filter.Evaluate(CreateObservation(frame, DateTimeOffset.UtcNow.AddSeconds(1)), settings);

        Assert.True(first.ShouldAnalyze);
        Assert.True(first.FrameChanged);
        Assert.NotNull(first.ScreenFrameHash);

        Assert.False(second.FrameChanged);
        Assert.False(second.ShouldAnalyze);
        Assert.Equal(first.ScreenFrameHash, second.ScreenFrameHash);
    }

    [Fact]
    public void Evaluate_WithVisiblyChangedFrame_ReportsChange()
    {
        var filter = new PerceptualHashChangeFilter();
        var settings = new DetectionSettings
        {
            FrameChangeThreshold = 2,
            MinRecheckIntervalSeconds = 120,
        };

        _ = filter.Evaluate(CreateObservation(DetectionTestImageFactory.CreateSolidJpeg(Color.Black), DateTimeOffset.UtcNow), settings);
        var changed = filter.Evaluate(CreateObservation(DetectionTestImageFactory.CreateSolidJpeg(Color.White), DateTimeOffset.UtcNow.AddSeconds(1)), settings);

        Assert.True(changed.FrameChanged);
        Assert.True(changed.ShouldAnalyze);
    }

    private static DetectionObservation CreateObservation(byte[] frame, DateTimeOffset ts) =>
        new()
        {
            StudentId = "student-001",
            TimestampUtc = ts,
            FrameBytes = frame,
        };
}
