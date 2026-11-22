using System;
using System.IO;
using SkiaSharp;
using ThemeStudio3DS.Models;

namespace ThemeStudio3DS.Services;

public static class CropRenderer
{
    public static (int Width, int Height)? ScreenSize(string id) =>
        id is ThemeImportService.Top or ThemeImportService.Bottom ? ThemeImportService.ExpectedSize(id) : null;

    public static byte[] Render(SKBitmap source, int width, int height, ImageCrop crop)
    {
        if (!crop.IsValid || width <= 0 || height <= 0)
            throw new InvalidDataException("Paramètres de recadrage invalides.");

        using var result = new SKBitmap(width, height);
        using var canvas = new SKCanvas(result);
        using var image = SKImage.FromBitmap(source);
        canvas.Clear(SKColors.Transparent);

        double scale = Math.Max((double)width / source.Width, (double)height / source.Height) * crop.Zoom;
        double scaledWidth = source.Width * scale;
        double scaledHeight = source.Height * scale;
        double left = (width - scaledWidth) * (crop.X + 1) / 2;
        double top = (height - scaledHeight) * (crop.Y + 1) / 2;
        var target = new SKRect((float)left, (float)top, (float)(left + scaledWidth), (float)(top + scaledHeight));

        canvas.DrawImage(image, target, new SKSamplingOptions(SKFilterMode.Linear));
        canvas.Flush();
        using var encoded = result.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
}
