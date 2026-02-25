namespace Controledu.Student.Host.Services;

/// <summary>
/// Coordinates controlled Student.Host shutdown requests.
/// </summary>
public interface IHostControlService
{
    /// <summary>
    /// Raised when existing UI should be shown/activated.
    /// </summary>
    event Action? ShowRequested;

    /// <summary>
    /// Raised when protected shutdown is requested.
    /// </summary>
    event Action? ShutdownRequested;

    /// <summary>
    /// Requests host shutdown.
    /// </summary>
    void RequestShutdown();

    /// <summary>
    /// Requests host UI activation.
    /// </summary>
    void RequestShow();
}

internal sealed class HostControlService : IHostControlService
{
    public event Action? ShowRequested;
    public event Action? ShutdownRequested;

    public void RequestShutdown() => ShutdownRequested?.Invoke();
    public void RequestShow() => ShowRequested?.Invoke();
}
