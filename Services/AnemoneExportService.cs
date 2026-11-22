using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using SkiaSharp;
using ThemeStudio3DS.Models;

namespace ThemeStudio3DS.Services;

public static class AnemoneExportService
{
    private const int HeaderSize = 0xD0;
    private const int TextureWidth = 512;
    private const int TextureHeight = 256;
    private const int TextureSize = TextureWidth * TextureHeight * sizeof(ushort);
    private const int BodyCacheSize = 0x150000;

    public static void Save(string path, ThemeProject project, bool overwrite = false)
    {
        if (!Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choisissez un nom de fichier avec l’extension .zip.");

        var body = Lz11Encoder.Encode(BuildBody(project));
        if (body.Length > BodyCacheSize)
            throw new InvalidDataException("Le thème dépasse la taille acceptée par Anemone3DS.");

        string target = Path.GetFullPath(path);
        string temporary = Path.Combine(Path.GetDirectoryName(target)!, $".anemone-{Guid.NewGuid():N}.tmp");
        bool created = false;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                using var zip = new ZipArchive(output, ZipArchiveMode.Create);
                using (var entry = zip.CreateEntry("body_LZ.bin", CompressionLevel.NoCompression).Open())
                    entry.Write(body);
                using (var entry = zip.CreateEntry("info.smdh").Open())
                    entry.Write(BuildMetadata(project));
            }
            File.Move(temporary, target, overwrite);
        }
        finally
        {
            if (created)
                File.Delete(temporary);
        }
    }

    private static byte[] BuildMetadata(ThemeProject project)
    {
        var metadata = new byte[0x36C0];
        "SMDH"u8.CopyTo(metadata);
        for (int language = 0; language < 16; language++)
        {
            int offset = 8 + language * 0x200;
            WriteTitle(project.Name, metadata.AsSpan(offset, 0x80));
            WriteTitle(project.Author, metadata.AsSpan(offset + 0x180, 0x80));
        }
        return metadata;
    }

    private static void WriteTitle(string text, Span<byte> field)
    {
        // Reserve a UTF-16 terminator and let the encoder stop at a complete character.
        Encoding.Unicode.GetEncoder().Convert(text.AsSpan(), field[..^2], true, out _, out _, out _);
    }

    private static byte[] BuildBody(ThemeProject project)
    {
        var top = RequireBackground(project, ThemeImportService.Top, "supérieur");
        var bottom = RequireBackground(project, ThemeImportService.Bottom, "inférieur");
        var body = new byte[HeaderSize + 2 * TextureSize];
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x00), 1); // Layout version
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x0C), 3); // Top draw type: texture
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x10), 1); // Top frame type: static
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x18), HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x20), 3); // Bottom draw type: texture
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x24), 1); // Bottom frame type: static
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x28), HeaderSize + TextureSize);

        WriteTexture(body.AsSpan(HeaderSize, TextureSize), top, ThemeImportService.ExpectedSize(ThemeImportService.Top)!.Value, border: 6);
        WriteTexture(body.AsSpan(HeaderSize + TextureSize, TextureSize), bottom, ThemeImportService.ExpectedSize(ThemeImportService.Bottom)!.Value, border: 0);
        return body;
    }

    private static ThemeResource RequireBackground(ThemeProject project, string id, string screen) =>
        project.Resources.SingleOrDefault(resource => resource.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException($"Ajoutez le fond {screen} avant d’exporter le thème.");

    private static void WriteTexture(Span<byte> destination, ThemeResource resource, (int Width, int Height) size, int border)
    {
        using var source = SKBitmap.Decode(resource.Png)
            ?? throw new InvalidDataException($"Impossible de lire l’image « {resource.FileName} ».");
        var (width, height) = size;
        using var rendered = SKBitmap.Decode(CropRenderer.Render(source, width, height, resource.Crop ?? ImageCrop.Centered));
        using var opaque = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(opaque))
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(rendered, 0, 0);
        }

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width + 2 * border; x++)
        {
            var color = opaque.GetPixel(Math.Clamp(x - border, 0, width - 1), y);
            var rgb565 = (ushort)((color.Red >> 3) << 11 | (color.Green >> 2) << 5 | (color.Blue >> 3));
            int tile = (y / 8) * (TextureWidth / 8) + x / 8;
            int withinTile = (x & 1) | ((y & 1) << 1) |
                ((x & 2) << 1) | ((y & 2) << 2) | ((x & 4) << 2) | ((y & 4) << 3);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[((tile * 64 + withinTile) * 2)..], rgb565);
        }
    }
}
