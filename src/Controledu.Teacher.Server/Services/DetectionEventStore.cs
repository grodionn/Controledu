using Controledu.Transport.Dto;
using System.Collections.Concurrent;

namespace Controledu.Teacher.Server.Services;

/// <summary>
/// In-memory alert store for AI detection events.
/// </summary>
public interface IDetectionEventStore
{
    /// <summary>
    /// Adds event to memory buffer.
    /// </summary>
    void Add(AlertEventDto alertEvent);

    /// <summary>
    /// Returns latest events in reverse-chronological order.
    /// </summary>
    IReadOnlyList<AlertEventDto> GetLatest(int take);
}

internal sealed class DetectionEventStore : IDetectionEventStore
{
    private readonly ConcurrentQueue<AlertEventDto> _events = new();
    private readonly int _capacity;

    public DetectionEventStore(int capacity = 1500)
    {
        _capacity = Math.Max(100, capacity);
    }

    public void Add(AlertEventDto alertEvent)
    {
        _events.Enqueue(alertEvent);
        while (_events.Count > _capacity)
        {
            _events.TryDequeue(out _);
        }
    }

    public IReadOnlyList<AlertEventDto> GetLatest(int take)
    {
        var boundedTake = Math.Clamp(take, 1, 1000);
        return _events
            .Reverse()
            .Take(boundedTake)
            .ToArray();
    }
}
