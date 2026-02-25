using Controledu.Student.Agent.Models;
using Controledu.Student.Agent.Options;
using Microsoft.Extensions.Options;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Controledu.Student.Agent.Services;

/// <summary>
/// Captures primary desktop frames for streaming.
/// </summary>
public interface IScreenCaptureService
{
    /// <summary>
    /// Captures primary-screen frame with desired JPEG quality.
    /// </summary>
    Task<ScreenCaptureResult?> CaptureAsync(int jpegQuality, CancellationToken cancellationToken = default);
}

internal sealed class ScreenCaptureService(IOptions<StudentAgentOptions> options) : IScreenCaptureService
{
    private const int EnumCurrentSettings = -1;

    public Task<ScreenCaptureResult?> CaptureAsync(int jpegQuality, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<ScreenCaptureResult?>(null);
        }

        // Capture only the primary display. Remote-control coordinates are normalized
        // against the streamed frame and mapped to the primary display on the student PC.
        // Virtual-screen capture can include empty regions (multi-monitor offsets),
        // which causes black bands and broken pointer mapping.
        var left = 0;
        var top = 0;
        var width = 0;
        var height = 0;

        if (!TryGetPrimaryDisplayPhysicalBounds(out left, out top, out width, out height))
        {
            width = GetSystemMetrics(0);
            height = GetSystemMetrics(1);
        }

        if (width <= 0 || height <= 0)
        {
            width = GetSystemMetrics(78);
            height = GetSystemMetrics(79);
            left = GetSystemMetrics(76);
            top = GetSystemMetrics(77);
        }

        if (width <= 0 || height <= 0)
        {
            return Task.FromResult<ScreenCaptureResult?>(null);
        }

        using var sourceBitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(sourceBitmap))
        {
            graphics.CopyFromScreen(left, top, 0, 0, new Size(width, height));
        }

        var maxWidth = Math.Max(320, options.Value.MaxCaptureWidth);
        var maxHeight = Math.Max(180, options.Value.MaxCaptureHeight);
        var (targetWidth, targetHeight) = ComputeTargetSize(width, height, maxWidth, maxHeight);

        using var encodedBitmap = targetWidth == width && targetHeight == height
            ? (Bitmap)sourceBitmap.Clone()
            : Resize(sourceBitmap, targetWidth, targetHeight);

        using var memory = new MemoryStream();
        var encoder = ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
        using var encoderParameters = new EncoderParameters(1);
        encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, Math.Clamp(jpegQuality, 10, 100));
        encodedBitmap.Save(memory, encoder, encoderParameters);

        var payload = memory.ToArray();
        return Task.FromResult<ScreenCaptureResult?>(new ScreenCaptureResult(payload, targetWidth, targetHeight, "jpeg"));
    }

    private static Bitmap Resize(Bitmap source, int width, int height)
    {
        var resized = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(resized);
        graphics.InterpolationMode = InterpolationMode.Bilinear;
        graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
        graphics.CompositingQuality = CompositingQuality.HighSpeed;
        graphics.DrawImage(source, 0, 0, width, height);
        return resized;
    }

    private static (int Width, int Height) ComputeTargetSize(int sourceWidth, int sourceHeight, int maxWidth, int maxHeight)
    {
        if (sourceWidth <= maxWidth && sourceHeight <= maxHeight)
        {
            return (sourceWidth, sourceHeight);
        }

        var ratioX = (double)maxWidth / sourceWidth;
        var ratioY = (double)maxHeight / sourceHeight;
        var ratio = Math.Min(ratioX, ratioY);

        var width = Math.Max(1, (int)Math.Round(sourceWidth * ratio));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * ratio));
        return (width, height);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

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
                // Use physical pixel size from current display mode to avoid DPI virtualization (125%/150% scaling).
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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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
