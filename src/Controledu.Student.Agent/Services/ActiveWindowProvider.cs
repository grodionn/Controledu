using Controledu.Common.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Controledu.Student.Agent.Services;

/// <summary>
/// Provides active window/process observation snapshots.
/// </summary>
public interface IActiveWindowProvider
{
    /// <summary>
    /// Captures current active window observation.
    /// </summary>
    Observation CaptureObservation();
}

internal sealed class ActiveWindowProvider : IActiveWindowProvider
{
    public Observation CaptureObservation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new Observation(DateTimeOffset.UtcNow, null, null, null);
        }

        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return new Observation(DateTimeOffset.UtcNow, null, null, null);
        }

        _ = GetWindowThreadProcessId(foreground, out var processId);
        var titleBuilder = new StringBuilder(512);
        _ = GetWindowText(foreground, titleBuilder, titleBuilder.Capacity);

        string? processName = null;
        try
        {
            processName = Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            // ignored
        }

        var title = string.IsNullOrWhiteSpace(titleBuilder.ToString()) ? null : titleBuilder.ToString();
        return new Observation(DateTimeOffset.UtcNow, title, processName, null);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
