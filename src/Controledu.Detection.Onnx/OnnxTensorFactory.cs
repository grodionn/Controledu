using Microsoft.ML.OnnxRuntime.Tensors;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Controledu.Detection.Onnx;

internal static class OnnxTensorFactory
{
    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Std = [0.229f, 0.224f, 0.225f];

    public static DenseTensor<float> CreateNormalizedTensor(byte[] imageBytes, int width, int height)
    {
        using var input = new MemoryStream(imageBytes, writable: false);
        using var image = Image.FromStream(input, useEmbeddedColorManagement: false, validateImageData: true);
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.DrawImage(image, new Rectangle(0, 0, width, height));

        var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });
        var bounds = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

        try
        {
            var stride = data.Stride;
            var bytesPerRow = Math.Abs(stride);
            var bytes = new byte[bytesPerRow * height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);

            for (var y = 0; y < height; y++)
            {
                var sourceY = stride > 0 ? y : (height - 1 - y);
                var rowOffset = sourceY * bytesPerRow;

                for (var x = 0; x < width; x++)
                {
                    var offset = rowOffset + (x * 3);
                    var b = bytes[offset] / 255f;
                    var g = bytes[offset + 1] / 255f;
                    var r = bytes[offset + 2] / 255f;

                    tensor[0, 0, y, x] = (r - Mean[0]) / Std[0];
                    tensor[0, 1, y, x] = (g - Mean[1]) / Std[1];
                    tensor[0, 2, y, x] = (b - Mean[2]) / Std[2];
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return tensor;
    }
}
