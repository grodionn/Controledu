using Controledu.Common.Runtime;
using Controledu.Student.Agent.Models;
using Controledu.Storage.Stores;
using Controledu.Transport.Dto;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Controledu.Student.Agent.Services;

public interface IRemoteControlService
{
    Task ProcessAsync(
        ResolvedStudentBinding binding,
        string deviceDisplayName,
        StudentHubClient hubClient,
        ISettingsStore settingsStore,
        CancellationToken cancellationToken);
}

public interface IRemoteControlInputExecutor
{
    void Execute(RemoteControlInputCommandDto command);
}

internal sealed class RemoteControlService(
    IRemoteControlInputExecutor inputExecutor,
    ILogger<RemoteControlService> logger) : IRemoteControlService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private PendingSessionState? _pending;
    private ActiveSessionState? _active;

    public async Task ProcessAsync(
        ResolvedStudentBinding binding,
        string deviceDisplayName,
        StudentHubClient hubClient,
        ISettingsStore settingsStore,
        CancellationToken cancellationToken)
    {
        while (hubClient.TryDequeueRemoteControlSessionCommand(out var sessionCommand))
        {
            await HandleSessionCommandAsync(binding, deviceDisplayName, hubClient, settingsStore, sessionCommand, cancellationToken);
        }

        await TryProcessPendingApprovalAsync(binding, deviceDisplayName, hubClient, settingsStore, cancellationToken);
        await TryProcessExpiryAsync(binding, deviceDisplayName, hubClient, settingsStore, cancellationToken);

        var budget = 64;
        while (budget-- > 0 && hubClient.TryDequeueRemoteControlInputCommand(out var inputCommand))
        {
            await HandleInputCommandAsync(binding, deviceDisplayName, hubClient, settingsStore, inputCommand, cancellationToken);
        }
    }

    private async Task HandleSessionCommandAsync(
        ResolvedStudentBinding binding,
        string deviceDisplayName,
        StudentHubClient hubClient,
        ISettingsStore settingsStore,
        RemoteControlSessionCommandDto command,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(command.ClientId, binding.ClientId, StringComparison.Ordinal))
        {
            return;
        }

        switch (command.Action)
        {
            case RemoteControlSessionAction.RequestStart:
                await StartPendingSessionAsync(deviceDisplayName, hubClient, settingsStore, command, cancellationToken);
                break;

            case RemoteControlSessionAction.Stop:
                await EndSessionAsync(
                    deviceDisplayName,
                    hubClient,
                    settingsStore,
                    command.SessionId,
                    RemoteControlSessionState.Ended,
                    "Session closed by teacher.",
                    cancellationToken);
                break;
        }
    }

    private async Task StartPendingSessionAsync(
        string deviceDisplayName,
        StudentHubClient hubClient,
        ISettingsStore settingsStore,
        RemoteControlSessionCommandDto command,
        CancellationToken cancellationToken)
    {
        _active = null;
        _pending = new PendingSessionState(
            command,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(5, command.ApprovalTimeoutSeconds)));

        await settingsStore.SetAsync(DetectionSettingKeys.RemoteControlDecisionJson, string.Empty, cancellationToken);
        await settingsStore.SetAsync(DetectionSettingKeys.RemoteControlRequestJson, JsonSerializer.Serialize(command, JsonOptions), cancellationToken);

        await hubClient.SendRemoteControlStatusAsync(
            new RemoteControlSessionStatusDto(
                command.ClientId,
                deviceDisplayName,
                command.SessionId,
                RemoteControlSessionState.PendingApproval,
                DateTimeOffset.UtcNow,
                "Waiting for student confirmation."),
            cancellationToken);

        logger.LogInformation("Remote control session {SessionId} pending approval", command.SessionId);
    }

    private async Task TryProcessPendingApprovalAsync(
        ResolvedStudentBinding binding,
        string deviceDisplayName,
        StudentHubClient hubClient,
        ISettingsStore settingsStore,
        CancellationToken cancellationToken)
    {
        if (_pending is null)
        {
            return;
        }

        var raw = await settingsStore.GetAsync(DetectionSettingKeys.RemoteControlDecisionJson, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        RemoteControlApprovalDecisionDto? decision;
        try
        {
            decision = JsonSerializer.Deserialize<RemoteControlApprovalDecisionDto>(raw, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invalid remote control decision payload.");
            await settingsStore.SetAsync(DetectionSettingKeys.RemoteControlDecisionJson, string.Empty, cancellationToken);
            return;
        }

        if (decision is null || !string.Equals(decision.SessionId, _pending.Command.SessionId, StringComparison.Ordinal))
        {
            return;
        }

        await settingsStore.SetAsync(DetectionSettingKeys.RemoteControlDecisionJson, string.Empty, cancellationToken);

        if (!decision.Approved)
        {
            await EndSessionAsync(
                deviceDisplayName,
                hubClient,
                settingsStore,
                _pending.Command.SessionId,
                RemoteControlSessionState.Rejected,
                decision.Message ?? "Student rejected remote control request.",
                cancellationToken);
            return;
        }

        _active = new ActiveSessionState(
            binding.ClientId,
            _pending.Command.SessionId,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(15, _pending.Command.MaxSessionSeconds)));
        _pending = null;
        await settingsStore.SetAsync(DetectionSettingKeys.RemoteControlRequestJson, string.Empty, cancellationToken);

        await hubClient.SendRemoteControlStatusAsync(
            new RemoteControlSessionStatusDto(
                binding.ClientId,
                deviceDisplayName,
                _active.SessionId,
                RemoteControlSessionState.Approved,
                DateTimeOffset.UtcNow,
                "Student approved remote control."),
            cancellationToken);

        logger.LogInformation("Remote control session {SessionId} approved", _active.SessionId);
    }

    private async Task TryProcessExpiryAsync(
        ResolvedStudentBinding binding,
        string deviceDisplayName,
        StudentHubClient hubClient,
        ISettingsStore settingsStore,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;

        if (_pending is not null && nowUtc >= _pending.ApprovalDeadlineUtc)
        {
            await EndSessionAsync(
                deviceDisplayName,
                hubClient,
                settingsStore,
                _pending.Command.SessionId,
                RemoteControlSessionState.Expired,
                "Student approval timeout.",
                cancellationToken);
        }

        if (_active is not null && nowUtc >= _active.ExpiresAtUtc)
        {
            await EndSessionAsync(
                deviceDisplayName,
                hubClient,
                settingsStore,
                _active.SessionId,
                RemoteControlSessionState.Expired,
                "Remote control session expired.",
                cancellationToken);
        }
    }

    private async Task HandleInputCommandAsync(
        ResolvedStudentBinding binding,
        string deviceDisplayName,
        StudentHubClient hubClient,
        ISettingsStore settingsStore,
        RemoteControlInputCommandDto command,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(command.ClientId, binding.ClientId, StringComparison.Ordinal))
        {
            return;
        }

        if (_active is null || !string.Equals(command.SessionId, _active.SessionId, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            inputExecutor.Execute(command);
            _active.LastInputAtUtc = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Remote input command failed for session {SessionId}", command.SessionId);
            await EndSessionAsync(
                deviceDisplayName,
                hubClient,
                settingsStore,
                command.SessionId,
                RemoteControlSessionState.Error,
                ex.Message,
                cancellationToken);
        }
    }

    private async Task EndSessionAsync(
        string deviceDisplayName,
        StudentHubClient hubClient,
        ISettingsStore settingsStore,
        string sessionId,
        RemoteControlSessionState state,
        string message,
        CancellationToken cancellationToken)
    {
        var pending = _pending;
        var active = _active;
        var isKnown = string.Equals(pending?.Command.SessionId, sessionId, StringComparison.Ordinal)
            || string.Equals(active?.SessionId, sessionId, StringComparison.Ordinal);
        if (!isKnown)
        {
            return;
        }

        _pending = null;
        _active = null;
        await settingsStore.SetAsync(DetectionSettingKeys.RemoteControlRequestJson, string.Empty, cancellationToken);
        await settingsStore.SetAsync(DetectionSettingKeys.RemoteControlDecisionJson, string.Empty, cancellationToken);

        await hubClient.SendRemoteControlStatusAsync(
            new RemoteControlSessionStatusDto(
                pending?.Command.ClientId ?? active?.ClientId ?? string.Empty,
                deviceDisplayName,
                sessionId,
                state,
                DateTimeOffset.UtcNow,
                message),
            cancellationToken);

        logger.LogInformation("Remote control session {SessionId} ended: {State} ({Message})", sessionId, state, message);
    }

    private sealed record PendingSessionState(RemoteControlSessionCommandDto Command, DateTimeOffset ApprovalDeadlineUtc);

    private sealed class ActiveSessionState
    {
        public ActiveSessionState(string clientId, string sessionId, DateTimeOffset startedAtUtc, DateTimeOffset expiresAtUtc)
        {
            ClientId = clientId;
            SessionId = sessionId;
            StartedAtUtc = startedAtUtc;
            ExpiresAtUtc = expiresAtUtc;
            LastInputAtUtc = startedAtUtc;
        }

        public string ClientId { get; }
        public string SessionId { get; }
        public DateTimeOffset StartedAtUtc { get; }
        public DateTimeOffset ExpiresAtUtc { get; }
        public DateTimeOffset LastInputAtUtc { get; set; }
    }
}

internal sealed class WindowsRemoteControlInputExecutor : IRemoteControlInputExecutor
{
    private const int EnumCurrentSettings = -1;
    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventWheel = 0x0800;
    private const uint KeyEventKeyUp = 0x0002;

    public void Execute(RemoteControlInputCommandDto command)
    {
        switch (command.Kind)
        {
            case RemoteControlInputKind.MouseMove:
                MovePointer(command.X, command.Y);
                break;
            case RemoteControlInputKind.MouseDown:
                MovePointer(command.X, command.Y);
                MouseButton(command.Button, isDown: true);
                break;
            case RemoteControlInputKind.MouseUp:
                MovePointer(command.X, command.Y);
                MouseButton(command.Button, isDown: false);
                break;
            case RemoteControlInputKind.MouseWheel:
                MovePointer(command.X, command.Y);
                mouse_event(MouseEventWheel, 0, 0, (uint)Math.Clamp(command.WheelDelta, -2400, 2400), UIntPtr.Zero);
                break;
            case RemoteControlInputKind.KeyDown:
                SendKey(command, isDown: true);
                break;
            case RemoteControlInputKind.KeyUp:
                SendKey(command, isDown: false);
                break;
        }
    }

    private static void MovePointer(double x, double y)
    {
        var left = 0;
        var top = 0;
        var width = 0;
        var height = 0;

        if (!TryGetPrimaryDisplayPhysicalBounds(out left, out top, out width, out height))
        {
            width = Math.Max(1, GetSystemMetrics(0));
            height = Math.Max(1, GetSystemMetrics(1));
        }

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        var px = left + (int)Math.Round(Math.Clamp(x, 0, 1) * (width - 1));
        var py = top + (int)Math.Round(Math.Clamp(y, 0, 1) * (height - 1));
        _ = SetCursorPos(px, py);
        mouse_event(MouseEventMove, 0, 0, 0, UIntPtr.Zero);
    }

    private static void MouseButton(RemoteMouseButton button, bool isDown)
    {
        var flag = button switch
        {
            RemoteMouseButton.Left => isDown ? MouseEventLeftDown : MouseEventLeftUp,
            RemoteMouseButton.Right => isDown ? MouseEventRightDown : MouseEventRightUp,
            RemoteMouseButton.Middle => isDown ? MouseEventMiddleDown : MouseEventMiddleUp,
            _ => 0U,
        };

        if (flag != 0)
        {
            mouse_event(flag, 0, 0, 0, UIntPtr.Zero);
        }
    }

    private static void SendKey(RemoteControlInputCommandDto command, bool isDown)
    {
        if (!TryMapVirtualKey(command.Key, command.Code, out var vk))
        {
            return;
        }

        if (IsModifierKey(vk))
        {
            keybd_event(vk, 0, isDown ? 0U : KeyEventKeyUp, UIntPtr.Zero);
            return;
        }

        if (isDown)
        {
            ApplyModifiers(command, isDown: true);
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            return;
        }

        keybd_event(vk, 0, KeyEventKeyUp, UIntPtr.Zero);
        ApplyModifiers(command, isDown: false);
    }

    private static void ApplyModifiers(RemoteControlInputCommandDto command, bool isDown)
    {
        var flag = isDown ? 0U : KeyEventKeyUp;

        if (command.Ctrl)
        {
            keybd_event(0x11, 0, flag, UIntPtr.Zero);
        }

        if (command.Alt)
        {
            keybd_event(0x12, 0, flag, UIntPtr.Zero);
        }

        if (command.Shift)
        {
            keybd_event(0x10, 0, flag, UIntPtr.Zero);
        }
    }

    private static bool IsModifierKey(byte vk) => vk is 0x10 or 0x11 or 0x12;

    private static bool TryMapVirtualKey(string? key, string? code, out byte vk)
    {
        vk = 0;
        var normalizedCode = string.IsNullOrWhiteSpace(code) ? string.Empty : code.Trim();
        if (!string.IsNullOrEmpty(normalizedCode) && TryMapVirtualKeyByCode(normalizedCode, out vk))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var normalized = key.Trim();

        switch (normalized)
        {
            case "Enter": vk = 0x0D; return true;
            case "Escape": vk = 0x1B; return true;
            case "Backspace": vk = 0x08; return true;
            case "Tab": vk = 0x09; return true;
            case " ": 
            case "Space":
            case "Spacebar": vk = 0x20; return true;
            case "ArrowLeft": vk = 0x25; return true;
            case "ArrowUp": vk = 0x26; return true;
            case "ArrowRight": vk = 0x27; return true;
            case "ArrowDown": vk = 0x28; return true;
            case "Delete": vk = 0x2E; return true;
            case "Home": vk = 0x24; return true;
            case "End": vk = 0x23; return true;
            case "PageUp": vk = 0x21; return true;
            case "PageDown": vk = 0x22; return true;
            case "Shift": vk = 0x10; return true;
            case "Control": vk = 0x11; return true;
            case "Alt": vk = 0x12; return true;
        }

        if (normalized.Length == 1)
        {
            var ch = normalized[0];
            if (char.IsLetter(ch))
            {
                vk = (byte)char.ToUpperInvariant(ch);
                return true;
            }

            if (char.IsDigit(ch))
            {
                vk = (byte)ch;
                return true;
            }

            var vks = VkKeyScan(ch);
            if (vks != -1)
            {
                vk = (byte)(vks & 0xFF);
                return true;
            }
        }

        return false;
    }

    private static bool TryMapVirtualKeyByCode(string code, out byte vk)
    {
        vk = 0;
        switch (code)
        {
            case "Enter":
            case "NumpadEnter": vk = 0x0D; return true;
            case "Escape": vk = 0x1B; return true;
            case "Backspace": vk = 0x08; return true;
            case "Tab": vk = 0x09; return true;
            case "Space": vk = 0x20; return true;
            case "ArrowLeft": vk = 0x25; return true;
            case "ArrowUp": vk = 0x26; return true;
            case "ArrowRight": vk = 0x27; return true;
            case "ArrowDown": vk = 0x28; return true;
            case "Delete": vk = 0x2E; return true;
            case "Home": vk = 0x24; return true;
            case "End": vk = 0x23; return true;
            case "PageUp": vk = 0x21; return true;
            case "PageDown": vk = 0x22; return true;
            case "ShiftLeft":
            case "ShiftRight": vk = 0x10; return true;
            case "ControlLeft":
            case "ControlRight": vk = 0x11; return true;
            case "AltLeft":
            case "AltRight": vk = 0x12; return true;
            case "CapsLock": vk = 0x14; return true;
            case "Insert": vk = 0x2D; return true;
            case "MetaLeft":
            case "MetaRight": vk = 0x5B; return true;
            case "ContextMenu": vk = 0x5D; return true;
            case "Minus": vk = 0xBD; return true;
            case "Equal": vk = 0xBB; return true;
            case "BracketLeft": vk = 0xDB; return true;
            case "BracketRight": vk = 0xDD; return true;
            case "Backslash": vk = 0xDC; return true;
            case "Semicolon": vk = 0xBA; return true;
            case "Quote": vk = 0xDE; return true;
            case "Comma": vk = 0xBC; return true;
            case "Period": vk = 0xBE; return true;
            case "Slash": vk = 0xBF; return true;
            case "Backquote": vk = 0xC0; return true;
            case "NumpadAdd": vk = 0x6B; return true;
            case "NumpadSubtract": vk = 0x6D; return true;
            case "NumpadMultiply": vk = 0x6A; return true;
            case "NumpadDivide": vk = 0x6F; return true;
            case "NumpadDecimal": vk = 0x6E; return true;
            case "Numpad0": vk = 0x60; return true;
            case "Numpad1": vk = 0x61; return true;
            case "Numpad2": vk = 0x62; return true;
            case "Numpad3": vk = 0x63; return true;
            case "Numpad4": vk = 0x64; return true;
            case "Numpad5": vk = 0x65; return true;
            case "Numpad6": vk = 0x66; return true;
            case "Numpad7": vk = 0x67; return true;
            case "Numpad8": vk = 0x68; return true;
            case "Numpad9": vk = 0x69; return true;
        }

        if (code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal))
        {
            var ch = code[3];
            if (ch is >= 'A' and <= 'Z')
            {
                vk = (byte)ch;
                return true;
            }
        }

        if (code.Length == 6 && code.StartsWith("Digit", StringComparison.Ordinal))
        {
            var ch = code[5];
            if (ch is >= '0' and <= '9')
            {
                vk = (byte)ch;
                return true;
            }
        }

        if (code.Length >= 2 && code[0] == 'F' && int.TryParse(code[1..], out var fn) && fn is >= 1 and <= 24)
        {
            vk = (byte)(0x70 + (fn - 1));
            return true;
        }

        return false;
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);

    private static bool TryGetPrimaryDisplayPhysicalBounds(out int left, out int top, out int width, out int height)
    {
        left = 0;
        top = 0;
        width = 0;
        height = 0;

        try
        {
            var monitor = MonitorFromPoint(new POINT(0, 0), 2 /* MONITOR_DEFAULTTOPRIMARY */);
            var monitorInfo = MONITORINFO.Create();
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref monitorInfo))
            {
                left = monitorInfo.rcMonitor.Left;
                top = monitorInfo.rcMonitor.Top;
                width = monitorInfo.rcMonitor.Right - monitorInfo.rcMonitor.Left;
                height = monitorInfo.rcMonitor.Bottom - monitorInfo.rcMonitor.Top;
            }

            var mode = DEVMODE.Create();
            if (EnumDisplaySettings(null, EnumCurrentSettings, ref mode))
            {
                width = mode.dmPelsWidth > 0 ? (int)mode.dmPelsWidth : width;
                height = mode.dmPelsHeight > 0 ? (int)mode.dmPelsHeight : height;
            }

            return width > 0 && height > 0;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;

        public POINT(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        public static MONITORINFO Create()
        {
            var value = new MONITORINFO();
            value.cbSize = Marshal.SizeOf<MONITORINFO>();
            return value;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;

        public static DEVMODE Create()
        {
            var value = new DEVMODE
            {
                dmDeviceName = string.Empty,
                dmFormName = string.Empty,
            };
            value.dmSize = (ushort)Marshal.SizeOf<DEVMODE>();
            return value;
        }
    }
}
