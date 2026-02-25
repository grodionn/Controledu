using Controledu.Common.Runtime;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Controledu.Student.Host;

/// <summary>
/// Compact always-on-top overlay window rendered via WebView2 (chat + hand-raise UI).
/// </summary>
public sealed class StudentOverlayWebViewForm : Form
{
    private const int OverlayWidth = 520;
    private const int CollapsedOverlayWidth = 280;
    private const int OverlayHeight = 520;
    private const int CollapsedOverlayHeight = 166;
    private const int MinOverlayWidth = 380;
    private const int MinOverlayHeight = 300;
    private const int TopMargin = 10;
    private const int RightMargin = 10;
    private const int CornerRadius = 18;
    private const int ResizeBorder = 8;

    private readonly string _overlayUrl;
    private readonly string _webViewUserDataPath;
    private readonly WebView2 _webView;
    private Size _expandedSize = new(OverlayWidth, OverlayHeight);
    private bool _isCollapsed;
    private bool _hasUserMoved;

    private const int WmNchitTest = 0x0084;
    private const int WmNclButtonDown = 0x00A1;
    private const int HtClient = 0x01;
    private const int HtCaption = 0x02;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    public StudentOverlayWebViewForm(string overlayUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(overlayUrl);
        _overlayUrl = overlayUrl;
        _webViewUserDataPath = Path.Combine(AppPaths.GetBasePath(), "webview2", "student-overlay");
        Directory.CreateDirectory(_webViewUserDataPath);

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Width = OverlayWidth;
        Height = OverlayHeight;
        MinimumSize = new Size(MinOverlayWidth, MinOverlayHeight);
        BackColor = Color.FromArgb(18, 18, 18);
        Padding = new Padding(1);
        DoubleBuffered = true;

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.FromArgb(18, 18, 18),
            Margin = new Padding(0),
        };

        Controls.Add(_webView);

        Shown += OnShownAsync;
        SizeChanged += (_, _) =>
        {
            if (!_isCollapsed && Width >= MinOverlayWidth && Height >= MinOverlayHeight)
            {
                _expandedSize = Size;
            }

            ApplyRoundedShape();
            if (_hasUserMoved)
            {
                ClampOverlayToWorkingArea();
            }
            else
            {
                PositionOverlay();
            }
        };
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNchitTest)
        {
            base.WndProc(ref m);

            if (_isCollapsed || (int)m.Result != HtClient)
            {
                return;
            }

            var hit = HitTestResizeBorder(GetPointFromLParam(m.LParam));
            if (hit != HtClient)
            {
                m.Result = (nint)hit;
            }

            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplyRoundedShape();
        PositionOverlay();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    private async void OnShownAsync(object? sender, EventArgs e)
    {
        if (_webView.CoreWebView2 is not null)
        {
            return;
        }

        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _webViewUserDataPath);
            await _webView.EnsureCoreWebView2Async(environment);
            var core = _webView.CoreWebView2;
            if (core is null)
            {
                return;
            }

            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            await core.AddScriptToExecuteOnDocumentCreatedAsync("window.addEventListener('contextmenu', e => e.preventDefault());");
            core.WebMessageReceived += OnWebMessageReceived;
            _webView.Source = new Uri(_overlayUrl, UriKind.Absolute);
        }
        catch
        {
            // Keep empty overlay window if WebView2 failed; main host will still run.
        }
    }

    private void PositionOverlay()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        var x = area.Right - Width - RightMargin;
        var y = area.Top + TopMargin;
        Location = new Point(Math.Max(area.Left, x), Math.Max(area.Top, y));
    }

    private void ApplyRoundedShape()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        Region?.Dispose();
        using var path = new GraphicsPath();
        var rect = new Rectangle(0, 0, Width, Height);
        var radius = Math.Max(8, Math.Min(CornerRadius, Math.Min(Width, Height) / 2));
        var diameter = radius * 2;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        Region = new Region(path);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            var type = typeElement.GetString();
            if (string.Equals(type, "overlayLayout", StringComparison.Ordinal))
            {
                if (!root.TryGetProperty("collapsed", out var collapsedElement))
                {
                    return;
                }

                ApplyCollapsedState(collapsedElement.ValueKind == JsonValueKind.True);
                return;
            }

            if (string.Equals(type, "overlayDragStart", StringComparison.Ordinal))
            {
                BeginWindowDrag();
            }
        }
        catch
        {
            // Ignore malformed messages from the overlay page.
        }
    }

    private void ApplyCollapsedState(bool collapsed)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => ApplyCollapsedState(collapsed)));
            return;
        }

        if (_isCollapsed == collapsed)
        {
            return;
        }

        _isCollapsed = collapsed;
        var nextSize = collapsed
            ? new Size(CollapsedOverlayWidth, CollapsedOverlayHeight)
            : new Size(
                Math.Max(MinOverlayWidth, _expandedSize.Width),
                Math.Max(MinOverlayHeight, _expandedSize.Height));

        if (Size != nextSize)
        {
            Size = nextSize;
            return;
        }

        ApplyRoundedShape();
        ClampOverlayToWorkingArea();
    }

    private void BeginWindowDrag()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(BeginWindowDrag));
            return;
        }

        _hasUserMoved = true;
        _ = ReleaseCapture();
        _ = SendMessage(Handle, WmNclButtonDown, (nint)HtCaption, 0);
    }

    private void ClampOverlayToWorkingArea()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        var maxX = Math.Max(area.Left, area.Right - Width - RightMargin);
        var maxY = Math.Max(area.Top, area.Bottom - Height - TopMargin);
        var minX = area.Left + RightMargin;
        var minY = area.Top + TopMargin;

        var nextX = Math.Max(minX, Math.Min(Location.X, maxX));
        var nextY = Math.Max(minY, Math.Min(Location.Y, maxY));
        Location = new Point(nextX, nextY);
    }

    private int HitTestResizeBorder(Point screenPoint)
    {
        var point = PointToClient(screenPoint);
        var onLeft = point.X <= ResizeBorder;
        var onRight = point.X >= Width - ResizeBorder;
        var onTop = point.Y <= ResizeBorder;
        var onBottom = point.Y >= Height - ResizeBorder;

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

        if (onLeft)
        {
            return HtLeft;
        }

        if (onRight)
        {
            return HtRight;
        }

        if (onTop)
        {
            return HtTop;
        }

        if (onBottom)
        {
            return HtBottom;
        }

        return HtClient;
    }

    private static Point GetPointFromLParam(nint lParam)
    {
        var value = lParam.ToInt64();
        var x = (short)(value & 0xFFFF);
        var y = (short)((value >> 16) & 0xFFFF);
        return new Point(x, y);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);
}
