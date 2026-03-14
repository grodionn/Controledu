using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Controledu.Host.Core;

/// <summary>
/// Shared window chrome helpers for desktop hosts.
/// </summary>
public static class HostWindowChrome
{
    public const int WmNclButtonDown = 0xA1;
    public const int HtCaption = 0x2;
    public const int HtClient = 0x1;
    public const int HtLeft = 10;
    public const int HtRight = 11;
    public const int HtTop = 12;
    public const int HtTopLeft = 13;
    public const int HtTopRight = 14;
    public const int HtBottom = 15;
    public const int HtBottomLeft = 16;
    public const int HtBottomRight = 17;

    /// <summary>
    /// Creates styled window action button.
    /// </summary>
    public static Button CreateWindowButton(string text, bool isClose)
    {
        var button = new Button
        {
            Text = text,
            Width = 45,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0),
            ForeColor = Color.FromArgb(234, 234, 234),
            BackColor = Color.FromArgb(20, 20, 20),
            Font = new Font("Segoe MDL2 Assets", 9.2F, FontStyle.Regular),
            TabStop = false,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };

        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = isClose ? Color.FromArgb(232, 17, 35) : Color.FromArgb(46, 46, 46);
        button.FlatAppearance.MouseDownBackColor = isClose ? Color.FromArgb(191, 20, 35) : Color.FromArgb(62, 62, 62);
        return button;
    }

    /// <summary>
    /// Enables dragging a borderless form by control area.
    /// </summary>
    public static void EnableDrag(Control control, nint windowHandle)
    {
        control.MouseDown += (_, eventArgs) =>
        {
            if (eventArgs.Button != MouseButtons.Left)
            {
                return;
            }

            _ = ReleaseCapture();
            _ = SendMessage(windowHandle, WmNclButtonDown, (nint)HtCaption, 0);
        };
    }

    /// <summary>
    /// Resolves resize hit-test result for borderless window.
    /// </summary>
    public static int HitTestResizeBorder(Form form, Point screenPoint, int resizeBorder)
    {
        var point = form.PointToClient(screenPoint);
        var clientSize = form.ClientSize;

        var onLeft = point.X <= resizeBorder;
        var onRight = point.X >= clientSize.Width - resizeBorder;
        var onTop = point.Y <= resizeBorder;
        var onBottom = point.Y >= clientSize.Height - resizeBorder;

        if (onTop && onLeft)
        {
            return HtTopLeft;
        }

        if (onTop && onRight)
        {
            return HtTopRight;
        }

        if (onBottom && onLeft)
        {
            return HtBottomLeft;
        }

        if (onBottom && onRight)
        {
            return HtBottomRight;
        }

        if (onTop)
        {
            return HtTop;
        }

        if (onBottom)
        {
            return HtBottom;
        }

        if (onLeft)
        {
            return HtLeft;
        }

        if (onRight)
        {
            return HtRight;
        }

        return HtClient;
    }

    /// <summary>
    /// Decodes point from native LPARAM.
    /// </summary>
    public static Point GetPointFromLParam(nint lParam)
    {
        var value = lParam.ToInt64();
        var x = (short)(value & 0xFFFF);
        var y = (short)((value >> 16) & 0xFFFF);
        return new Point(x, y);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);
}
