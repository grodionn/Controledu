using Microsoft.ML.OnnxRuntime.Tensors;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Controledu.Detection.Onnx;

internal static class OnnxTensorFactory
{
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
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                tensor[0, 0, y, x] = pixel.R / 255f;
                tensor[0, 1, y, x] = pixel.G / 255f;
                tensor[0, 2, y, x] = pixel.B / 255f;
            }
        }

        return tensor;
    }
}
