using System;
using System.IO;
using SkiaSharp;
using ThemeStudio3DS.Models;

namespace ThemeStudio3DS.Services;

public static class PngReader
{
    public const int MaxBytes = 20 * 1024 * 1024;

    public static byte[] ReadBounded(Stream input, int limit)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = input.Read(buffer)) > 0)
        {
            if (output.Length + count > limit)
                throw new InvalidDataException("Fichier trop volumineux.");
            output.Write(buffer, 0, count);
        }

        return output.ToArray();
    }

    public static ThemeResource Read(string id, string name, Stream input) =>
        Decode(id, name, ReadBounded(input, MaxBytes));

    public static ThemeResource Decode(string id, string name, byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat != SKEncodedImageFormat.Png)
            throw new InvalidDataException($"PNG invalide : {name}.");

        var info = codec.Info;
        if (info.Width is <= 0 or > 4096 || info.Height is <= 0 or > 4096 ||
            (long)info.Width * info.Height > 8_388_608)
            throw new InvalidDataException($"Dimensions PNG hors limites : {name}.");

        using var bitmap = new SKBitmap(info.Width, info.Height);
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException($"PNG illisible : {name}.");

        return new ThemeResource(id, name, bytes, info.Width, info.Height);
    }
}
