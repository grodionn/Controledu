namespace Controledu.Teacher.Server.Services;

/// <summary>
/// Coordinates desktop host UI activation requests.
/// </summary>
public interface IHostControlService
{
    /// <summary>
    /// Raised when existing UI should be shown/activated.
    /// </summary>
    event Action? ShowRequested;

    /// <summary>
    /// Requests host UI activation.
    /// </summary>
    void RequestShow();
}

internal sealed class HostControlService : IHostControlService
{
    public event Action? ShowRequested;

    public void RequestShow() => ShowRequested?.Invoke();
}
