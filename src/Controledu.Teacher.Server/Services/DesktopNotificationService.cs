namespace Controledu.Teacher.Server.Services;

/// <summary>
/// Notification message for desktop host bridge.
/// </summary>
public sealed record DesktopNotificationMessage(
    string Title,
    string Message,
    string Kind,
    DateTimeOffset TimestampUtc);

/// <summary>
/// Publishes desktop notifications to the embedded host shell.
/// </summary>
public interface IDesktopNotificationService
{
    /// <summary>
    /// Event raised when notification should be shown by desktop shell.
    /// </summary>
    event EventHandler<DesktopNotificationMessage>? Published;

    /// <summary>
    /// Publishes a message.
    /// </summary>
    void Publish(DesktopNotificationMessage message);
}

internal sealed class DesktopNotificationService : IDesktopNotificationService
{
    /// <inheritdoc />
    public event EventHandler<DesktopNotificationMessage>? Published;

    /// <inheritdoc />
    public void Publish(DesktopNotificationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Published?.Invoke(this, message);
    }
}
