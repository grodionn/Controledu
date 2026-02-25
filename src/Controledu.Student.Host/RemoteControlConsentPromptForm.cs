using Controledu.Student.Host.Services;
using System.Drawing.Drawing2D;

namespace Controledu.Student.Host;

/// <summary>
/// Local consent prompt for remote control requests. Shown near the hand-raise overlay.
/// </summary>
public sealed class RemoteControlConsentPromptForm : Form
{
    private readonly System.Windows.Forms.Timer _countdownTimer;
    private readonly Label _countdownLabel;
    private readonly Button _allowButton;
    private int _secondsRemaining;

    public RemoteControlConsentPromptForm(RemoteControlConsentPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        _secondsRemaining = Math.Max(5, prompt.ApprovalTimeoutSeconds);

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(16, 16, 16);
        ForeColor = Color.FromArgb(238, 238, 238);
        Width = 360;
        Height = 152;
        Padding = new Padding(0);

        var root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(18, 18, 18),
            Padding = new Padding(12),
        };
        root.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, root.Width - 1, root.Height - 1);
            using var path = CreateRoundedPath(rect, 14);
            using var pen = new Pen(Color.FromArgb(46, 46, 46));
            e.Graphics.DrawPath(pen, path);
        };

        var title = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Text = "Удалённый доступ",
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(242, 242, 242),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var description = new Label
        {
            Dock = DockStyle.Top,
            Height = 54,
            Text = $"Преподаватель запрашивает управление компьютером.\nСессия: до {Math.Max(15, prompt.MaxSessionSeconds)} сек.",
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
            ForeColor = Color.FromArgb(188, 188, 188),
            TextAlign = ContentAlignment.TopLeft,
        };

        _countdownLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 18,
            Text = string.Empty,
            Font = new Font("Segoe UI", 8F, FontStyle.Regular),
            ForeColor = Color.FromArgb(255, 197, 94),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 38,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.Transparent,
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        _allowButton = CreateButton("Разрешить", Color.FromArgb(52, 138, 255), Color.White);
        _allowButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Yes;
            Close();
        };

        var denyButton = CreateButton("Отклонить", Color.FromArgb(34, 34, 34), Color.FromArgb(236, 236, 236));
        denyButton.FlatAppearance.BorderSize = 1;
        denyButton.FlatAppearance.BorderColor = Color.FromArgb(62, 62, 62);
        denyButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.No;
            Close();
        };

        buttons.Controls.Add(denyButton, 0, 0);
        buttons.Controls.Add(_allowButton, 1, 0);

        root.Controls.Add(buttons);
        root.Controls.Add(_countdownLabel);
        root.Controls.Add(description);
        root.Controls.Add(title);
        Controls.Add(root);

        _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _countdownTimer.Tick += (_, _) =>
        {
            _secondsRemaining = Math.Max(0, _secondsRemaining - 1);
            UpdateCountdownText();
            if (_secondsRemaining <= 0)
            {
                DialogResult = DialogResult.No;
                Close();
            }
        };

        Shown += (_, _) =>
        {
            ApplyRoundedRegion(this, 14);
            UpdateCountdownText();
            _countdownTimer.Start();
            Activate();
        };
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _countdownTimer.Stop();
        _countdownTimer.Dispose();
        base.OnFormClosed(e);
    }

    public void PositionBelow(Rectangle anchorBounds)
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        var preferredX = anchorBounds.Width > 0
            ? anchorBounds.Left + ((anchorBounds.Width - Width) / 2)
            : area.Left + ((area.Width - Width) / 2);
        var preferredY = anchorBounds.Height > 0
            ? anchorBounds.Bottom + 10
            : area.Top + 64;

        var maxX = area.Right - Width - 4;
        var maxY = area.Bottom - Height - 4;
        Location = new Point(
            Math.Max(area.Left + 4, Math.Min(preferredX, maxX)),
            Math.Max(area.Top + 4, Math.Min(preferredY, maxY)));
    }

    private void UpdateCountdownText()
    {
        _countdownLabel.Text = $"Подтвердите в течение {_secondsRemaining} сек.";
        _allowButton.Text = _secondsRemaining > 0 ? $"Разрешить ({_secondsRemaining})" : "Разрешить";
    }

    private static Button CreateButton(string text, Color backColor, Color foreColor)
    {
        var button = new Button
        {
            Dock = DockStyle.Fill,
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = foreColor,
            Font = new Font("Segoe UI", 8.75F, FontStyle.Bold),
            Margin = new Padding(4, 0, 0, 0),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            TabStop = false,
        };

        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(backColor, 0.08F);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(backColor, 0.08F);
        return button;
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var safeRadius = Math.Max(1, Math.Min(radius, Math.Min(bounds.Width / 2, bounds.Height / 2)));
        var diameter = safeRadius * 2;

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
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
}
