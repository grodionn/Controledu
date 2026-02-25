namespace Controledu.Common.IO;

/// <summary>
/// Chunking and resume helper functions.
/// </summary>
public static class ChunkingUtility
{
    /// <summary>
    /// Default chunk size (256KB).
    /// </summary>
    public const int DefaultChunkSize = 256 * 1024;

    /// <summary>
    /// Calculates number of chunks required for file size.
    /// </summary>
    public static int GetChunkCount(long fileSize, int chunkSize = DefaultChunkSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fileSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSize);

        return (int)Math.Ceiling(fileSize / (double)chunkSize);
    }

    /// <summary>
    /// Returns missing chunk indexes for resume scenarios.
    /// </summary>
    public static IReadOnlyList<int> GetMissingChunks(int totalChunks, IEnumerable<int> existingChunkIndexes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalChunks);

        ArgumentNullException.ThrowIfNull(existingChunkIndexes);

        var existing = new HashSet<int>(existingChunkIndexes.Where(index => index >= 0 && index < totalChunks));
        var missing = new List<int>(totalChunks);

        for (var i = 0; i < totalChunks; i++)
        {
            if (!existing.Contains(i))
            {
                missing.Add(i);
            }
        }

        return missing;
    }
}
