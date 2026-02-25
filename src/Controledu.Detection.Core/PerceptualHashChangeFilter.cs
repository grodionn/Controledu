using Controledu.Detection.Abstractions;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Controledu.Detection.Core;

/// <summary>
/// Average-hash frame-change filter for low-cost pre-screening.
/// </summary>
public sealed class PerceptualHashChangeFilter : IFrameChangeFilter
{
    private readonly object _sync = new();
    private string? _lastHash;
    private DateTimeOffset _lastAnalyzedAtUtc = DateTimeOffset.MinValue;

    /// <inheritdoc />
    public FrameChangeFilterResult Evaluate(DetectionObservation observation, DetectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(settings);

        if (observation.FrameBytes is null || observation.FrameBytes.Length == 0)
        {
            return new FrameChangeFilterResult(null, true, true);
        }

        var hash = ComputeAverageHash(observation.FrameBytes);
        if (hash is null)
        {
            return new FrameChangeFilterResult(null, true, true);
        }

        lock (_sync)
        {
            if (_lastHash is null)
            {
                _lastHash = hash;
                _lastAnalyzedAtUtc = observation.TimestampUtc;
                return new FrameChangeFilterResult(hash, true, true);
            }

            var distance = HammingDistance(_lastHash, hash);
            var frameChanged = distance > Math.Max(1, settings.FrameChangeThreshold);
            var recheckInterval = TimeSpan.FromSeconds(Math.Max(1, settings.MinRecheckIntervalSeconds));
            var forceRecheck = observation.TimestampUtc - _lastAnalyzedAtUtc >= recheckInterval;

            if (frameChanged || forceRecheck)
            {
                _lastHash = hash;
                _lastAnalyzedAtUtc = observation.TimestampUtc;
                return new FrameChangeFilterResult(hash, frameChanged, true);
            }

            return new FrameChangeFilterResult(hash, false, false);
        }
    }

    private static string? ComputeAverageHash(byte[] imageBytes)
    {
        try
        {
            using var input = new MemoryStream(imageBytes, writable: false);
            using var image = Image.FromStream(input, useEmbeddedColorManagement: false, validateImageData: true);
            using var bitmap = new Bitmap(8, 8, PixelFormat.Format24bppRgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.DrawImage(image, new Rectangle(0, 0, 8, 8));

            Span<byte> grays = stackalloc byte[64];
            var sum = 0;
            var index = 0;
            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 8; x++)
                {
                    var color = bitmap.GetPixel(x, y);
                    var gray = (byte)((color.R * 299 + color.G * 587 + color.B * 114) / 1000);
                    grays[index++] = gray;
                    sum += gray;
                }
            }

            var average = sum / 64;
            ulong bits = 0;
            for (var i = 0; i < 64; i++)
            {
                if (grays[i] >= average)
                {
                    bits |= 1UL << i;
                }
            }

            return bits.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static int HammingDistance(string leftHex, string rightHex)
    {
        if (!ulong.TryParse(leftHex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var left)
            || !ulong.TryParse(rightHex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var right))
        {
            return int.MaxValue;
        }

        var value = left ^ right;
        var count = 0;
        while (value != 0)
        {
            value &= value - 1;
            count++;
        }

        return count;
    }
}
