using System.Drawing;
using System.Drawing.Imaging;

namespace Controledu.Tests;

internal static class DetectionTestImageFactory
{
    public static byte[] CreateSolidJpeg(Color color, int width = 128, int height = 72, long quality = 90)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(color);
        }

        using var stream = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().First(static encoder => encoder.FormatID == ImageFormat.Jpeg.Guid);
        using var encoderParameters = new EncoderParameters(1);
        encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        bitmap.Save(stream, codec, encoderParameters);
        return stream.ToArray();
    }
}
