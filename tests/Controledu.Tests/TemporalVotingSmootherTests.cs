using Controledu.Detection.Abstractions;
using Controledu.Detection.Core;

namespace Controledu.Tests;

public sealed class TemporalVotingSmootherTests
{
    [Fact]
    public void Apply_WithTwoPositivesOutOfThree_BecomesStablePositive()
    {
        var smoother = new TemporalVotingSmoother();
        var settings = new DetectionSettings
        {
            TemporalWindowSize = 3,
            TemporalRequiredVotes = 2,
            CooldownSeconds = 30,
        };

        var now = DateTimeOffset.UtcNow;
        var positive = CreatePositive(0.85);

        var first = smoother.Apply(positive, settings, now);
        var second = smoother.Apply(positive with { Confidence = 0.90 }, settings, now.AddSeconds(1));

        Assert.False(first.Result.IsStable);
        Assert.False(first.ShouldEmitAlert);

        Assert.True(second.Result.IsStable);
        Assert.True(second.Result.IsAiUiDetected);
        Assert.True(second.ShouldEmitAlert);
    }

    [Fact]
    public void Apply_CooldownSuppressesDuplicateAlerts()
    {
        var smoother = new TemporalVotingSmoother();
        var settings = new DetectionSettings
        {
            TemporalWindowSize = 1,
            TemporalRequiredVotes = 1,
            CooldownSeconds = 20,
        };

        var now = DateTimeOffset.UtcNow;
        var positive = CreatePositive(0.88);

        var first = smoother.Apply(positive, settings, now);
        var second = smoother.Apply(positive, settings, now.AddSeconds(2));
        var third = smoother.Apply(positive, settings, now.AddSeconds(25));

        Assert.True(first.ShouldEmitAlert);
        Assert.False(second.ShouldEmitAlert);
        Assert.True(third.ShouldEmitAlert);
    }

    private static DetectionResult CreatePositive(double confidence) =>
        new(
            IsAiUiDetected: true,
            Confidence: confidence,
            Class: DetectionClass.ChatGpt,
            StageSource: DetectionStageSource.MetadataRule,
            Reason: "test",
            ModelVersion: null,
            TriggeredKeywords: ["chatgpt"],
            IsStable: false);
}
