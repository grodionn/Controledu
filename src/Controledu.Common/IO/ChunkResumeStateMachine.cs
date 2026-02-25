namespace Controledu.Common.IO;

/// <summary>
/// Tracks chunk completion state for resumable transfers.
/// </summary>
public sealed class ChunkResumeStateMachine
{
    private readonly bool[] _completed;

    /// <summary>
    /// Creates state machine with total chunk count and optional completed indexes.
    /// </summary>
    public ChunkResumeStateMachine(int totalChunks, IEnumerable<int>? completedChunkIndexes = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalChunks);

        _completed = new bool[totalChunks];

        if (completedChunkIndexes is null)
        {
            return;
        }

        foreach (var index in completedChunkIndexes)
        {
            if (index >= 0 && index < _completed.Length)
            {
                _completed[index] = true;
            }
        }
    }

    /// <summary>
    /// Total chunk count.
    /// </summary>
    public int TotalChunks => _completed.Length;

    /// <summary>
    /// Number of completed chunks.
    /// </summary>
    public int CompletedCount => _completed.Count(static value => value);

    /// <summary>
    /// True when all chunks are completed.
    /// </summary>
    public bool IsComplete => CompletedCount == TotalChunks;

    /// <summary>
    /// Marks chunk index as completed.
    /// </summary>
    public void MarkCompleted(int chunkIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunkIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunkIndex, _completed.Length);

        _completed[chunkIndex] = true;
    }

    /// <summary>
    /// Returns missing chunk indexes sorted ascending.
    /// </summary>
    public IReadOnlyList<int> GetMissingChunks()
    {
        var missing = new List<int>(_completed.Length);
        for (var index = 0; index < _completed.Length; index++)
        {
            if (!_completed[index])
            {
                missing.Add(index);
            }
        }

        return missing;
    }
}
