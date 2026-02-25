using Controledu.Common.IO;

namespace Controledu.Tests;

public sealed class ChunkResumeTests
{
    [Fact]
    public void GetMissingChunks_ReturnsExpectedIndices()
    {
        var missing = ChunkingUtility.GetMissingChunks(8, new[] { 0, 2, 3, 7 });

        Assert.Equal([1, 4, 5, 6], missing);
    }

    [Fact]
    public void StateMachine_TracksResumeProgressUntilComplete()
    {
        var state = new ChunkResumeStateMachine(5, [0, 2]);

        Assert.Equal([1, 3, 4], state.GetMissingChunks());
        Assert.False(state.IsComplete);

        state.MarkCompleted(1);
        state.MarkCompleted(3);

        Assert.Equal([4], state.GetMissingChunks());

        state.MarkCompleted(4);

        Assert.True(state.IsComplete);
        Assert.Equal(5, state.CompletedCount);
    }

    [Fact]
    public void VerifySha256_MatchesExpectedHash()
    {
        var payload = System.Text.Encoding.UTF8.GetBytes("controledu");
        var hash = HashingUtility.Sha256Hex(payload);

        Assert.True(HashingUtility.VerifySha256(payload, hash));
    }
}
