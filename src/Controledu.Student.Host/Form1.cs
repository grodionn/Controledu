using Controledu.Common.Runtime;
using Controledu.Student.Host.Options;
using Controledu.Common.Updates;
using Controledu.Host.Core;
using Controledu.Student.Host.Services;
using Controledu.Transport.Dto;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.Drawing;

namespace Controledu.Student.Host;

public partial class Form1 : Form
{
    private const int WmNchitTest = 0x84;
    private const int HtClient = 0x1;
    private const int ResizeBorder = 8;

    private const string GlyphMinimize = "\uE921";
    private const string GlyphMaximize = "\uE922";
    private const string GlyphRestore = "\uE923";
    private const string GlyphClose = "\uE8BB";

    private readonly string _uiUrl;
    private readonly NotifyIcon _notifyIcon;
    private readonly IHostControlService _hostControlService;
    private readonly StudentOverlayWebViewForm _handRaiseOverlay;
    private readonly IRemoteControlConsentService _remoteControlConsentService;
    private readonly System.Windows.Forms.Timer _remoteControlConsentTimer;
    private readonly AutoUpdateOptions _autoUpdateOptions;
    private readonly HttpClient? _autoUpdateHttpClient;
    private readonly AutoUpdateClient? _autoUpdateClient;
    private readonly System.Windows.Forms.Timer? _autoUpdateTimer;
    private readonly string _webViewUserDataPath;
    private readonly Icon _applicationIcon;
    private Button? _maximizeButton;
    private bool _trayHintShown;
    private bool _allowExit;
    private bool _autoUpdateCheckInProgress;
    private Panel? _autoUpdatePromptPanel;
    private Label? _autoUpdatePromptLabel;
    private Button? _autoUpdatePromptInstallButton;
    private Button? _autoUpdatePromptLaterButton;
    private TaskCompletionSource<bool>? _autoUpdateApprovalTcs;
    private string? _autoUpdateApprovalVersion;
    private string? _autoUpdateDeferredVersion;
    private bool _remoteControlPromptActive;
    private string? _remoteControlPromptShownForSessionId;

    public Form1(
        string uiUrl,
        StudentHostOptions options,
        AutoUpdateOptions autoUpdateOptions,
        IHostControlService hostControlService,
        IHandRaiseRequestService handRaiseRequestService,
        IRemoteControlConsentService remoteControlConsentService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uiUrl);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(autoUpdateOptions);
        ArgumentNullException.ThrowIfNull(hostControlService);
        ArgumentNullException.ThrowIfNull(handRaiseRequestService);
        ArgumentNullException.ThrowIfNull(remoteControlConsentService);

        _uiUrl = uiUrl;
        _hostControlService = hostControlService;
        _remoteControlConsentService = remoteControlConsentService;
        _autoUpdateOptions = autoUpdateOptions;
        _webViewUserDataPath = Path.Combine(AppPaths.GetBasePath(), "webview2", "student-host");
        Directory.CreateDirectory(_webViewUserDataPath);

        if (_autoUpdateOptions.Enabled)
        {
            _autoUpdateHttpClient = new HttpClient();
            _autoUpdateClient = new AutoUpdateClient(_autoUpdateHttpClient);
            _autoUpdateTimer = new System.Windows.Forms.Timer
            {
                Interval = Math.Clamp(_autoUpdateOptions.CheckIntervalMinutes, 1, 24 * 60) * 60 * 1000,
            };
            _autoUpdateTimer.Tick += async (_, _) => await CheckForUpdatesAsync(startup: false);
        }

        InitializeComponent();
        webView.DefaultBackgroundColor = Color.FromArgb(24, 24, 24);

        Text = options.WindowTitle;
        _applicationIcon = LoadApplicationIcon();
        Icon = _applicationIcon;

        FormBorderStyle = FormBorderStyle.None;
        ControlBox = false;
        MinimumSize = new Size(980, 650);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(16, 16, 16);
        Padding = new Padding(0);
        SetStyle(ControlStyles.ResizeRedraw, true);

        if (!options.StartMaximized)
        {
            Size = new Size(1240, 820);
        }

        BuildCustomChrome();

        if (options.StartMaximized)
        {
            WindowState = FormWindowState.Maximized;
        }

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Open Controledu Endpoint", null, (_, _) => RestoreFromTray());

        _notifyIcon = new NotifyIcon
        {
            Icon = _applicationIcon,
            Text = "Controledu Endpoint is running in background",
            Visible = true,
            ContextMenuStrip = trayMenu,
        };

        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                RestoreFromTray();
            }
        };
        _hostControlService.ShutdownRequested += OnShutdownRequested;
        _hostControlService.ShowRequested += OnShowRequested;
        _ = handRaiseRequestService; // Hand-raise action is now rendered in the WebView overlay page.
        var overlayUrl = new Uri(new Uri(_uiUrl, UriKind.Absolute), "overlay.html").ToString();
        _handRaiseOverlay = new StudentOverlayWebViewForm(overlayUrl);

        _remoteControlConsentTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _remoteControlConsentTimer.Tick += async (_, _) => await CheckRemoteControlConsentAsync();
        _remoteControlConsentTimer.Start();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNchitTest && WindowState == FormWindowState.Normal)
        {
            base.WndProc(ref m);

            if ((int)m.Result == HtClient)
            {
                var hit = HitTestResizeBorder(GetPointFromLParam(m.LParam));
                if (hit != HtClient)
                {
                    m.Result = (nint)hit;
                }
            }

            return;
        }

        base.WndProc(ref m);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);

        try
        {
            var webViewEnvironment = await CoreWebView2Environment.CreateAsync(userDataFolder: _webViewUserDataPath);
            await webView.EnsureCoreWebView2Async(webViewEnvironment);
            ConfigureWebViewSettings(webView.CoreWebView2);
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.addEventListener('contextmenu', e => e.preventDefault());");
            webView.Source = new Uri(_uiUrl, UriKind.Absolute);
            if (!_handRaiseOverlay.Visible)
            {
                _handRaiseOverlay.Show();
            }
        }
        catch (Exception ex)
        {
            var message =
                "WebView2 runtime is missing or failed to initialize.\n\n" +
                "The local web UI will be opened in your default browser.\n\n" +
                ex.Message;

            MessageBox.Show(message, "Controledu Endpoint", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            try
            {
                Process.Start(new ProcessStartInfo(_uiUrl) { UseShellExecute = true });
            }
            catch
            {
                // Ignore secondary browser launch failure.
            }

            if (!_handRaiseOverlay.Visible)
            {
                _handRaiseOverlay.Show();
            }
        }

        StartAutoUpdateLoop();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (WindowState == FormWindowState.Minimized)
        {
            HideToTray();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _hostControlService.ShutdownRequested -= OnShutdownRequested;
        _hostControlService.ShowRequested -= OnShowRequested;
        _autoUpdateTimer?.Stop();
        _autoUpdateTimer?.Dispose();
        _autoUpdateClient?.Dispose();
        _autoUpdateHttpClient?.Dispose();
        _remoteControlConsentTimer.Stop();
        _remoteControlConsentTimer.Dispose();
        _autoUpdateApprovalTcs?.TrySetResult(false);
        if (!_handRaiseOverlay.IsDisposed)
        {
            _handRaiseOverlay.Close();
            _handRaiseOverlay.Dispose();
        }
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _applicationIcon.Dispose();
        base.OnFormClosed(e);
    }

    private void HideToTray()
    {
        Hide();

        if (_trayHintShown)
        {
            return;
        }

        _trayHintShown = true;
        _notifyIcon.BalloonTipTitle = "Controledu Endpoint";
        _notifyIcon.BalloonTipText = "Application keeps running in background.";
        _notifyIcon.ShowBalloonTip(2500);
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
    }

    private void OnShutdownRequested()
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke((Action)OnShutdownRequested);
            return;
        }

        _allowExit = true;
        Close();
    }

    private void OnShowRequested()
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke((Action)OnShowRequested);
            return;
        }

        RestoreFromTray();
    }

    private void BuildCustomChrome()
    {
        Controls.Remove(webView);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.Transparent,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var chromeBar = BuildChromeBar();

        var contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.FromArgb(14, 14, 14),
        };

        var autoUpdatePromptBanner = BuildAutoUpdatePromptBanner();
        webView.Dock = DockStyle.Fill;
        contentHost.Controls.Add(webView);
        contentHost.Controls.Add(autoUpdatePromptBanner);
        autoUpdatePromptBanner.BringToFront();

        root.Controls.Add(chromeBar, 0, 0);
        root.Controls.Add(contentHost, 0, 1);

        Controls.Add(root);
        root.BringToFront();

        Resize += (_, _) => UpdateMaximizeGlyph();
        UpdateMaximizeGlyph();
    }

    private Panel BuildChromeBar()
    {
        var chromeBar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(20, 20, 20),
            Margin = new Padding(0),
            Padding = new Padding(10, 5, 0, 5),
        };

        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.Transparent,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138F));

        var titleHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 0, 0, 0),
            Margin = new Padding(0),
            BackColor = Color.Transparent,
        };

        var titleLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Controledu",
            ForeColor = Color.FromArgb(236, 236, 236),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
            AutoEllipsis = true,
        };

        titleHost.Controls.Add(titleLabel);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.Transparent,
        };

        var minimizeButton = CreateWindowButton(GlyphMinimize, isClose: false);
        minimizeButton.Click += (_, _) => WindowState = FormWindowState.Minimized;

        _maximizeButton = CreateWindowButton(GlyphMaximize, isClose: false);
        _maximizeButton.Click += (_, _) => ToggleMaximizeState();

        var closeButton = CreateWindowButton(GlyphClose, isClose: true);
        closeButton.Click += (_, _) => HideToTray();

        actions.Controls.Add(minimizeButton);
        actions.Controls.Add(_maximizeButton);
        actions.Controls.Add(closeButton);

        row.Controls.Add(titleHost, 0, 0);
        row.Controls.Add(actions, 1, 0);
        chromeBar.Controls.Add(row);

        EnableDrag(chromeBar);
        EnableDrag(row);
        EnableDrag(titleHost);
        EnableDrag(titleLabel);

        titleHost.DoubleClick += (_, _) => ToggleMaximizeState();
        titleLabel.DoubleClick += (_, _) => ToggleMaximizeState();

        return chromeBar;
    }

    private static Button CreateWindowButton(string text, bool isClose) =>
        HostWindowChrome.CreateWindowButton(text, isClose);

    private void ToggleMaximizeState()
    {
        WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
        UpdateMaximizeGlyph();
    }

    private void UpdateMaximizeGlyph()
    {
        if (_maximizeButton is null)
        {
            return;
        }

        _maximizeButton.Text = WindowState == FormWindowState.Maximized
            ? GlyphRestore
            : GlyphMaximize;
    }

    private void EnableDrag(Control control) =>
        HostWindowChrome.EnableDrag(control, Handle);

    private int HitTestResizeBorder(Point screenPoint) =>
        HostWindowChrome.HitTestResizeBorder(this, screenPoint, ResizeBorder);

    private static Point GetPointFromLParam(nint lParam) =>
        HostWindowChrome.GetPointFromLParam(lParam);

    private static void ConfigureWebViewSettings(CoreWebView2 coreWebView2) =>
        HostWebViewRuntime.ConfigureLockedDownSettings(coreWebView2);

    private static Icon LoadApplicationIcon() =>
        HostWebViewRuntime.LoadApplicationIcon(SystemIcons.Shield);

    private void StartAutoUpdateLoop()
    {
        if (!_autoUpdateOptions.Enabled || _autoUpdateClient is null)
        {
            return;
        }

        _autoUpdateTimer?.Start();
        _ = CheckForUpdatesAsync(startup: true);
    }

    private async Task CheckForUpdatesAsync(bool startup)
    {
        if (!_autoUpdateOptions.Enabled || _autoUpdateClient is null || IsDisposed || Disposing)
        {
            return;
        }

        if (_autoUpdateCheckInProgress)
        {
            return;
        }

        _autoUpdateCheckInProgress = true;
        try
        {
            WriteAutoUpdateTrace($"Check started (startup={startup}).");
            if (startup)
            {
                var delaySeconds = Math.Clamp(_autoUpdateOptions.StartupDelaySeconds, 0, 300);
                if (delaySeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                }
            }

            var manifest = await _autoUpdateClient.FetchManifestAsync(
                _autoUpdateOptions.ManifestUrl,
                _autoUpdateOptions.DownloadTimeoutSeconds,
                CancellationToken.None);

            var currentVersion = ControleduVersion.GetDisplayVersion();
            WriteAutoUpdateTrace($"Current version: {currentVersion}; manifest version: {manifest.Version}");
            if (!AutoUpdateClient.IsUpdateAvailable(currentVersion, manifest))
            {
                WriteAutoUpdateTrace("No update available.");
                return;
            }

            if (string.Equals(_autoUpdateDeferredVersion, manifest.Version, StringComparison.OrdinalIgnoreCase))
            {
                WriteAutoUpdateTrace($"Update {manifest.Version} is available but deferred by user.");
                return;
            }

            var approved = await RequestAutoUpdateApprovalAsync(manifest.Version);
            if (!approved)
            {
                _autoUpdateDeferredVersion = manifest.Version;
                WriteAutoUpdateTrace($"Update {manifest.Version} deferred by user.");
                return;
            }

            _autoUpdateDeferredVersion = null;
            WriteAutoUpdateTrace($"User approved update {manifest.Version}. Downloading installer.");

            var installerPath = await _autoUpdateClient.DownloadInstallerAsync(
                manifest,
                "student-host",
                _autoUpdateOptions.DownloadTimeoutSeconds,
                CancellationToken.None);

            WriteAutoUpdateTrace($"Installer downloaded: {installerPath}");
            ShowAutoUpdateNotification($"Installing update {manifest.Version}...");
            if (!TryLaunchUpdater(installerPath, "student-host"))
            {
                WriteAutoUpdateTrace("Updater launch returned false.");
                return;
            }

            _allowExit = true;
            WriteAutoUpdateTrace("Updater launched successfully. Closing host.");
            if (!IsDisposed)
            {
                BeginInvoke(new Action(Close));
            }
        }
        catch (Exception ex)
        {
            WriteAutoUpdateTrace($"Check failed: {ex}");
            ShowAutoUpdateNotification($"Update check failed: {TrimNotification(ex.Message)}");
        }
        finally
        {
            _autoUpdateCheckInProgress = false;
        }
    }

    private bool TryLaunchUpdater(string installerPath, string productKey) =>
        HostAutoUpdateUtility.TryLaunchUpdater(
            installerPath,
            productKey,
            Application.ExecutablePath,
            Environment.ProcessId,
            WriteAutoUpdateTrace,
            ShowAutoUpdateNotification);

    private void ShowAutoUpdateNotification(string text)
    {
        var safeText = string.IsNullOrWhiteSpace(text) ? "Updating..." : text.Trim();
        _notifyIcon.BalloonTipTitle = "Controledu Update";
        _notifyIcon.BalloonTipText = safeText.Length > 200 ? safeText[..200] : safeText;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(2500);
    }

    private static string TrimNotification(string text) =>
        HostAutoUpdateUtility.TrimNotification(text);

    private void ShowAutoUpdateBannerStatus(string text, string bannerKind, bool showActions)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => ShowAutoUpdateBannerStatus(text, bannerKind, showActions)));
            return;
        }

        if (_autoUpdatePromptPanel is null || _autoUpdatePromptLabel is null || _autoUpdatePromptInstallButton is null || _autoUpdatePromptLaterButton is null)
        {
            return;
        }

        _autoUpdatePromptLabel.Text = text;
        _autoUpdatePromptInstallButton.Visible = showActions;
        _autoUpdatePromptLaterButton.Visible = showActions;
        _autoUpdatePromptInstallButton.Enabled = showActions;
        _autoUpdatePromptLaterButton.Enabled = showActions;

        switch (bannerKind)
        {
            case "success":
                _autoUpdatePromptPanel.BackColor = Color.FromArgb(191, 238, 201);
                _autoUpdatePromptLabel.ForeColor = Color.FromArgb(13, 65, 33);
                break;
            case "error":
                _autoUpdatePromptPanel.BackColor = Color.FromArgb(255, 206, 206);
                _autoUpdatePromptLabel.ForeColor = Color.FromArgb(98, 21, 21);
                break;
            default:
                _autoUpdatePromptPanel.BackColor = Color.FromArgb(255, 214, 102);
                _autoUpdatePromptLabel.ForeColor = Color.FromArgb(51, 35, 0);
                break;
        }

        SetAutoUpdatePromptVisible(true);
    }

    private Panel BuildAutoUpdatePromptBanner()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 58,
            Visible = false,
            Padding = new Padding(10, 8, 10, 8),
            Margin = new Padding(0),
            BackColor = Color.FromArgb(255, 214, 102),
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.Transparent,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var label = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(51, 35, 0),
            Text = "Update available.",
            Margin = new Padding(0, 0, 10, 0),
        };

        var installButton = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Text = "Update now",
            Margin = new Padding(0, 0, 8, 0),
            Padding = new Padding(10, 4, 10, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(255, 239, 170),
            ForeColor = Color.FromArgb(45, 30, 0),
            Cursor = Cursors.Hand,
            TabStop = false,
        };
        installButton.FlatAppearance.BorderSize = 1;
        installButton.FlatAppearance.BorderColor = Color.FromArgb(207, 160, 50);

        var laterButton = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Text = "Later",
            Margin = new Padding(0),
            Padding = new Padding(10, 4, 10, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(255, 229, 138),
            ForeColor = Color.FromArgb(45, 30, 0),
            Cursor = Cursors.Hand,
            TabStop = false,
        };
        laterButton.FlatAppearance.BorderSize = 1;
        laterButton.FlatAppearance.BorderColor = Color.FromArgb(207, 160, 50);

        installButton.Click += (_, _) => ResolveAutoUpdateApproval(true);
        laterButton.Click += (_, _) => ResolveAutoUpdateApproval(false);

        table.Controls.Add(label, 0, 0);
        table.Controls.Add(installButton, 1, 0);
        table.Controls.Add(laterButton, 2, 0);
        panel.Controls.Add(table);

        _autoUpdatePromptPanel = panel;
        _autoUpdatePromptLabel = label;
        _autoUpdatePromptInstallButton = installButton;
        _autoUpdatePromptLaterButton = laterButton;
        return panel;
    }

    private Task<bool> RequestAutoUpdateApprovalAsync(string version)
    {
        if (IsDisposed || Disposing)
        {
            return Task.FromResult(false);
        }

        if (_autoUpdateApprovalTcs is not null)
        {
            return _autoUpdateApprovalTcs.Task;
        }

        _autoUpdateApprovalVersion = version;
        ShowAutoUpdateBannerStatus($"Update {version} is available. Install now?", bannerKind: "info", showActions: true);

        _autoUpdateApprovalTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _autoUpdateApprovalTcs.Task;
    }

    private void ResolveAutoUpdateApproval(bool approved)
    {
        var tcs = _autoUpdateApprovalTcs;
        var version = _autoUpdateApprovalVersion;
        _autoUpdateApprovalTcs = null;
        _autoUpdateApprovalVersion = null;
        if (!approved && !string.IsNullOrWhiteSpace(version))
        {
            _autoUpdateDeferredVersion = version;
        }
        SetAutoUpdatePromptVisible(false);
        tcs?.TrySetResult(approved);
    }

    private void SetAutoUpdatePromptVisible(bool visible)
    {
        if (_autoUpdatePromptPanel is null)
        {
            return;
        }

        _autoUpdatePromptPanel.Visible = visible;
        _autoUpdatePromptPanel.BringToFront();
    }

    private void HideAutoUpdateBannerStatus()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(HideAutoUpdateBannerStatus));
            return;
        }

        _autoUpdateApprovalVersion = null;
        _autoUpdateApprovalTcs?.TrySetResult(false);
        _autoUpdateApprovalTcs = null;
        SetAutoUpdatePromptVisible(false);
    }

    private static void WriteAutoUpdateTrace(string message) =>
        HostAutoUpdateUtility.WriteAutoUpdateTrace("student-host", message);

    private async Task CheckRemoteControlConsentAsync()
    {
        if (_remoteControlPromptActive || IsDisposed)
        {
            return;
        }

        RemoteControlConsentPrompt? prompt;
        try
        {
            prompt = await _remoteControlConsentService.TryGetPendingPromptAsync();
        }
        catch
        {
            return;
        }

        if (prompt is null)
        {
            _remoteControlPromptShownForSessionId = null;
            return;
        }

        if (string.Equals(_remoteControlPromptShownForSessionId, prompt.SessionId, StringComparison.Ordinal))
        {
            return;
        }

        _remoteControlPromptActive = true;
        _remoteControlPromptShownForSessionId = prompt.SessionId;

        try
        {
            var timeoutSeconds = Math.Max(5, prompt.ApprovalTimeoutSeconds);
            var durationSeconds = Math.Max(15, prompt.MaxSessionSeconds);

            using var promptForm = new RemoteControlConsentPromptForm(prompt);
            var anchorBounds = (!_handRaiseOverlay.IsDisposed && _handRaiseOverlay.Visible)
                ? _handRaiseOverlay.Bounds
                : Rectangle.Empty;
            promptForm.PositionBelow(anchorBounds);

            var result = promptForm.ShowDialog();
            var approved = result == DialogResult.Yes;
            var decision = new RemoteControlApprovalDecisionDto(
                prompt.SessionId,
                Approved: approved,
                DecidedAtUtc: DateTimeOffset.UtcNow,
                Message: approved
                    ? $"Approved by local user. Session up to {durationSeconds}s. Approval timeout was {timeoutSeconds}s."
                    : "Rejected by local user.");

            await _remoteControlConsentService.SubmitDecisionAsync(decision);
        }
        catch
        {
            // Ignore local prompt failures; agent-side approval timeout will expire the request.
        }
        finally
        {
            _remoteControlPromptActive = false;
        }
    }
}
