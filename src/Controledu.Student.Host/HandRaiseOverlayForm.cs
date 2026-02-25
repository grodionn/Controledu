using Controledu.Student.Host.Services;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Controledu.Student.Host;

/// <summary>
/// Compact, top-centered overlay for hand-raise requests.
/// Auto-collapses to the top edge to avoid covering the student UI.
/// </summary>
public sealed class HandRaiseOverlayForm : Form
{
    private const string TitleText = "Поднять руку";
    private const string HintText = "Нажмите, чтобы позвать преподавателя";
    private const string ButtonText = "Позвать преподавателя";
    private const string SendingText = "Отправка сигнала...";
    private const string SentText = "Сигнал отправлен";
    private const string ErrorText = "Не удалось отправить сигнал";
    private const string ReadyStatusText = "Готово";
    private const string ReadyHeaderStatusText = "нажмите";
    private const string CooldownPrefixText = "Повтор через";
    private const string SecondsText = "с";

    private const int OverlayWidth = 320;
    private const int ExpandedHeight = 136;
    private const int CollapsedHeight = 42;
    private const int CollapsedVisibleHeight = 24;
    private const int HeaderHeight = 42;
    private const int TopMarginExpanded = 8;
    private const int CornerRadius = 14;
    private const int ButtonRadius = 10;
    private const int TimerIntervalMs = 250;
    private const int AutoCollapseDelaySeconds = 6;

    private static readonly Color FormBackground = Color.FromArgb(16, 16, 16);
    private static readonly Color HeaderBackground = Color.FromArgb(22, 22, 22);
    private static readonly Color BodyBackground = Color.FromArgb(18, 18, 18);
    private static readonly Color BorderColor = Color.FromArgb(46, 46, 46);
    private static readonly Color DividerColor = Color.FromArgb(34, 34, 34);
    private static readonly Color TextPrimary = Color.FromArgb(236, 236, 236);
    private static readonly Color TextMuted = Color.FromArgb(166, 166, 166);
    private static readonly Color Accent = Color.FromArgb(52, 138, 255);
    private static readonly Color AccentHover = Color.FromArgb(72, 152, 255);
    private static readonly Color AccentPressed = Color.FromArgb(38, 118, 228);
    private static readonly Color Success = Color.FromArgb(83, 201, 132);
    private static readonly Color Warning = Color.FromArgb(225, 171, 69);
    private static readonly Color Error = Color.FromArgb(224, 92, 92);

    private readonly IHandRaiseRequestService _handRaiseRequestService;
    private readonly Panel _rootPanel;
    private readonly Panel _headerPanel;
    private readonly Panel _bodyPanel;
    private readonly Label _titleLabel;
    private readonly Label _headerStatusLabel;
    private readonly Label _hintLabel;
    private readonly Label _statusLabel;
    private readonly Panel _statusDot;
    private readonly Button _toggleButton;
    private readonly Button _raiseHandButton;
    private readonly System.Windows.Forms.Timer _timer;

    private bool _isCollapsed;
    private bool _isSending;
    private DateTimeOffset _pinnedStatusUntilUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _autoCollapseAtUtc = DateTimeOffset.MinValue;
    private Color _currentStatusColor = Success;

    public HandRaiseOverlayForm(IHandRaiseRequestService handRaiseRequestService)
    {
        ArgumentNullException.ThrowIfNull(handRaiseRequestService);
        _handRaiseRequestService = handRaiseRequestService;

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Width = OverlayWidth;
        Height = ExpandedHeight;
        BackColor = FormBackground;
        ForeColor = TextPrimary;
        Opacity = 0.98;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        _rootPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = FormBackground,
            Padding = new Padding(0),
        };
        _rootPanel.Paint += OnRootPanelPaint;
        Controls.Add(_rootPanel);

        _headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = HeaderHeight,
            BackColor = HeaderBackground,
            Padding = new Padding(12, 0, 8, 0),
            Cursor = Cursors.Hand,
        };
        _headerPanel.Click += OnHeaderClicked;

        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = 3,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28F));
        headerLayout.Click += OnHeaderClicked;

        _titleLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = TitleText,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0),
            Cursor = Cursors.Hand,
        };
        _titleLabel.Click += OnHeaderClicked;

        _headerStatusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = ReadyHeaderStatusText,
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 8F, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleRight,
            AutoEllipsis = true,
            Margin = new Padding(0),
            Cursor = Cursors.Hand,
        };
        _headerStatusLabel.Click += OnHeaderClicked;

        _toggleButton = new Button
        {
            Dock = DockStyle.Fill,
            Text = "^",
            FlatStyle = FlatStyle.Flat,
            BackColor = HeaderBackground,
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Margin = new Padding(0),
            TabStop = false,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        _toggleButton.FlatAppearance.BorderSize = 0;
        _toggleButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(34, 34, 34);
        _toggleButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(42, 42, 42);
        _toggleButton.Click += (_, _) => ToggleCollapsedByUser();

        headerLayout.Controls.Add(_titleLabel, 0, 0);
        headerLayout.Controls.Add(_headerStatusLabel, 1, 0);
        headerLayout.Controls.Add(_toggleButton, 2, 0);
        _headerPanel.Controls.Add(headerLayout);

        _bodyPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BodyBackground,
            Padding = new Padding(12, 8, 12, 10),
            Margin = new Padding(0),
        };

        var divider = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = DividerColor,
            Margin = new Padding(0),
        };

        var bodyLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        bodyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
        bodyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        bodyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));

        _hintLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = HintText,
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 8.25F, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0),
        };

        var statusHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };

        _statusDot = new Panel
        {
            Width = 8,
            Height = 8,
            BackColor = Success,
            Margin = new Padding(0),
            Location = new Point(0, 6),
        };
        _statusDot.Paint += OnStatusDotPaint;

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = ReadyStatusText,
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 8.25F, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(14, 0, 0, 0),
            AutoEllipsis = true,
        };

        statusHost.Controls.Add(_statusLabel);
        statusHost.Controls.Add(_statusDot);

        _raiseHandButton = new Button
        {
            Dock = DockStyle.Fill,
            Text = ButtonText,
            FlatStyle = FlatStyle.Flat,
            BackColor = Accent,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            TabStop = false,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Margin = new Padding(0),
        };
        _raiseHandButton.FlatAppearance.BorderSize = 0;
        _raiseHandButton.FlatAppearance.MouseOverBackColor = AccentHover;
        _raiseHandButton.FlatAppearance.MouseDownBackColor = AccentPressed;
        _raiseHandButton.MinimumSize = new Size(0, 38);
        _raiseHandButton.Click += OnRaiseHandClicked;
        _raiseHandButton.MouseEnter += (_, _) => TouchInteraction();

        bodyLayout.Controls.Add(_hintLabel, 0, 0);
        bodyLayout.Controls.Add(statusHost, 0, 1);
        bodyLayout.Controls.Add(_raiseHandButton, 0, 2);

        _bodyPanel.Controls.Add(bodyLayout);

        _rootPanel.Controls.Add(_bodyPanel);
        _rootPanel.Controls.Add(divider);
        _rootPanel.Controls.Add(_headerPanel);

        _timer = new System.Windows.Forms.Timer { Interval = TimerIntervalMs };
        _timer.Tick += (_, _) => OnTimerTick();
        _timer.Start();

        SizeChanged += (_, _) =>
        {
            ApplyShapes();
            PositionOverlay();
        };
        _raiseHandButton.Resize += (_, _) => ApplyRoundedRegion(_raiseHandButton, ButtonRadius);

        MouseEnter += (_, _) => TouchInteraction();
        _rootPanel.MouseEnter += (_, _) => TouchInteraction();
        _bodyPanel.MouseEnter += (_, _) => TouchInteraction();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplyShapes();
        SetCollapsed(false, userAction: false);
        TouchInteraction();
        _autoCollapseAtUtc = DateTimeOffset.UtcNow.AddSeconds(4);
        RefreshState();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            return;
        }

        _timer.Stop();
        base.OnFormClosing(e);
    }

    private void OnTimerTick()
    {
        RefreshState();
        HandleAutoCollapse();
    }

    private void HandleAutoCollapse()
    {
        if (_isCollapsed || _isSending)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now < _autoCollapseAtUtc)
        {
            return;
        }

        if (Bounds.Contains(Cursor.Position))
        {
            _autoCollapseAtUtc = now.AddSeconds(2);
            return;
        }

        SetCollapsed(true, userAction: false);
    }

    private async void OnRaiseHandClicked(object? sender, EventArgs e)
    {
        if (_isSending)
        {
            return;
        }

        SetCollapsed(false, userAction: false);
        TouchInteraction();
        _isSending = true;
        _pinnedStatusUntilUtc = DateTimeOffset.MinValue;
        SetStatus(SendingText, "отправка", Accent, isMutedText: false);
        UpdateButtonState(TimeSpan.Zero);

        try
        {
            var result = await _handRaiseRequestService.RequestAsync();
            if (result.Accepted)
            {
                SetStatus(SentText, "отправлено", Success, isMutedText: false);
                PinStatus(TimeSpan.FromSeconds(2));
                ScheduleAutoCollapse(TimeSpan.FromSeconds(3));
            }
            else
            {
                var seconds = Math.Max(1, (int)Math.Ceiling(result.RetryAfter.TotalSeconds));
                SetCooldownStatus(seconds);
                ScheduleAutoCollapse(TimeSpan.FromSeconds(2));
            }
        }
        catch
        {
            SetStatus(ErrorText, "ошибка", Error, isMutedText: false);
            PinStatus(TimeSpan.FromSeconds(3));
            ScheduleAutoCollapse(TimeSpan.FromSeconds(4));
        }
        finally
        {
            _isSending = false;
            RefreshState();
        }
    }

    private void RefreshState()
    {
        var remaining = _handRaiseRequestService.GetRemainingCooldown();
        UpdateButtonState(remaining);

        if (_isSending)
        {
            return;
        }

        if (DateTimeOffset.UtcNow < _pinnedStatusUntilUtc)
        {
            return;
        }

        if (remaining <= TimeSpan.Zero)
        {
            SetStatus(ReadyStatusText, ReadyHeaderStatusText, Success, isMutedText: true);
            return;
        }

        var seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        SetCooldownStatus(seconds);
    }

    private void SetCooldownStatus(int seconds)
    {
        var text = $"{CooldownPrefixText} {seconds} {SecondsText}";
        var header = $"{seconds}с";
        SetStatus(text, header, Warning, isMutedText: false);
    }

    private void SetStatus(string bodyText, string headerText, Color dotColor, bool isMutedText)
    {
        _currentStatusColor = dotColor;
        _statusDot.BackColor = dotColor;
        _statusDot.Invalidate();

        _statusLabel.Text = bodyText;
        _statusLabel.ForeColor = isMutedText ? TextMuted : TextPrimary;

        _headerStatusLabel.Text = headerText;
        _headerStatusLabel.ForeColor = isMutedText ? TextMuted : TextPrimary;
    }

    private void UpdateButtonState(TimeSpan remaining)
    {
        var canSend = !_isSending && remaining <= TimeSpan.Zero;
        _raiseHandButton.Enabled = canSend;

        if (_isSending)
        {
            _raiseHandButton.Text = SendingText;
            return;
        }

        if (remaining > TimeSpan.Zero)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
            _raiseHandButton.Text = $"Повтор через {seconds}с";
            return;
        }

        _raiseHandButton.Text = ButtonText;
    }

    private void PinStatus(TimeSpan duration)
    {
        _pinnedStatusUntilUtc = DateTimeOffset.UtcNow.Add(duration);
    }

    private void ScheduleAutoCollapse(TimeSpan delay)
    {
        _autoCollapseAtUtc = DateTimeOffset.UtcNow.Add(delay);
    }

    private void TouchInteraction()
    {
        if (_isCollapsed)
        {
            return;
        }

        _autoCollapseAtUtc = DateTimeOffset.UtcNow.AddSeconds(AutoCollapseDelaySeconds);
    }

    private void OnHeaderClicked(object? sender, EventArgs e)
    {
        ToggleCollapsedByUser();
    }

    private void ToggleCollapsedByUser()
    {
        SetCollapsed(!_isCollapsed, userAction: true);
    }

    private void SetCollapsed(bool collapsed, bool userAction)
    {
        if (_isCollapsed == collapsed)
        {
            PositionOverlay();
            return;
        }

        _isCollapsed = collapsed;
        _bodyPanel.Visible = !collapsed;
        Height = collapsed ? CollapsedHeight : ExpandedHeight;
        _toggleButton.Text = collapsed ? "v" : "^";
        _hintLabel.Visible = !collapsed;

        if (!collapsed)
        {
            TouchInteraction();
        }
        else if (userAction)
        {
            _autoCollapseAtUtc = DateTimeOffset.MinValue;
        }

        ApplyShapes();
        PositionOverlay();
    }

    private void PositionOverlay()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        var x = area.Left + ((area.Width - Width) / 2);

        var y = _isCollapsed
            ? area.Top - Math.Max(0, Height - CollapsedVisibleHeight)
            : area.Top + TopMarginExpanded;

        Location = new Point(Math.Max(area.Left, x), y);
    }

    private void OnRootPanelPaint(object? sender, PaintEventArgs e)
    {
        var rect = _rootPanel.ClientRectangle;
        if (rect.Width <= 2 || rect.Height <= 2)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        rect = new Rectangle(rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
        using var path = CreateRoundedPath(rect, CornerRadius);
        using var pen = new Pen(BorderColor, 1f);
        e.Graphics.DrawPath(pen, path);
    }

    private void OnStatusDotPaint(object? sender, PaintEventArgs e)
    {
        var rect = _statusDot.ClientRectangle;
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(_currentStatusColor);
        e.Graphics.FillEllipse(brush, 0, 0, rect.Width - 1, rect.Height - 1);
    }

    private void ApplyShapes()
    {
        ApplyRoundedRegion(this, CornerRadius);
        ApplyRoundedRegion(_raiseHandButton, ButtonRadius);
    }

    private static void ApplyRoundedRegion(Control control, int radius)
    {
        if (control.Width <= 0 || control.Height <= 0)
        {
            return;
        }

        control.Region?.Dispose();
        using var path = CreateRoundedPath(new Rectangle(0, 0, control.Width, control.Height), radius);
        control.Region = new Region(path);
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var safeRadius = Math.Max(1, Math.Min(radius, Math.Min(bounds.Width / 2, bounds.Height / 2)));
        var diameter = safeRadius * 2;

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
