using Microsoft.Web.WebView2.Core;
using System.Drawing;
using System.Windows.Forms;

namespace Controledu.Host.Core;

/// <summary>
/// Shared helpers for WebView2 host runtime setup.
/// </summary>
public static class HostWebViewRuntime
{
    /// <summary>
    /// Applies locked-down WebView settings suitable for production desktop host.
    /// </summary>
    public static void ConfigureLockedDownSettings(CoreWebView2 coreWebView2)
    {
        coreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        coreWebView2.Settings.AreDevToolsEnabled = false;
        coreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
        coreWebView2.Settings.IsStatusBarEnabled = false;
        coreWebView2.Settings.IsZoomControlEnabled = false;
    }

    /// <summary>
    /// Loads executable icon with fallback.
    /// </summary>
    public static Icon LoadApplicationIcon(Icon fallback)
    {
        var extractedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (extractedIcon is not null)
        {
            return (Icon)extractedIcon.Clone();
        }

        return new Icon(fallback, fallback.Size);
    }
}
