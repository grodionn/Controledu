namespace Controledu.Teacher.Server.Services;

/// <summary>
/// Abstraction for system time to simplify testing.
/// </summary>
public interface ISystemClock
{
    /// <summary>
    /// Gets current UTC time.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}

internal sealed class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
