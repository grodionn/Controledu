using Controledu.Common.Runtime;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Drawing.Drawing2D;

namespace Controledu.Student.Host;

/// <summary>
/// Compact always-on-top overlay window rendered via WebView2 (chat + hand-raise UI).
/// </summary>
public sealed class StudentOverlayWebViewForm : Form
{
    private const int OverlayWidth = 420;
    private const int OverlayHeight = 520;
    private const int TopMargin = 10;
    private const int RightMargin = 10;
    private const int CornerRadius = 18;

    private readonly string _overlayUrl;
    private readonly string _webViewUserDataPath;
    private readonly WebView2 _webView;

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
            ApplyRoundedShape();
            PositionOverlay();
        };
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
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.addEventListener('contextmenu', e => e.preventDefault());");
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
}

