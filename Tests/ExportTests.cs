using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using SkiaSharp;
using ThemeStudio3DS.Models;
using ThemeStudio3DS.Services;

internal static class ExportTests
{
    public static void Run(string directory, Action<bool, string> check, Action<Action, string> reject)
    {
        using var top = new SKBitmap(400, 240);
        top.Erase(SKColors.Black);
        top.SetPixel(0, 0, SKColors.Red);
        top.SetPixel(1, 0, SKColors.Lime);
        top.SetPixel(2, 1, SKColors.Blue);
        top.SetPixel(0, 8, SKColors.White);
        top.SetPixel(399, 239, SKColors.Yellow);

        using var bottom = new SKBitmap(640, 240);
        bottom.Erase(SKColors.Red);
        using (var canvas = new SKCanvas(bottom))
        using (var paint = new SKPaint { Color = SKColors.Blue, BlendMode = SKBlendMode.Src })
        {
            canvas.DrawRect(320, 0, 320, 240, paint);
            paint.Color = new SKColor(255, 255, 255, 128);
            canvas.DrawRect(320, 0, 1, 1, paint);
        }

        var project = new ThemeProject("Export", "", "#123456",
            [Resource(ThemeImportService.Top, top), Resource(ThemeImportService.Bottom, bottom, new ImageCrop(1, 1, 0)),
             Resource("folder_01", top)]);
        string path = Path.Combine(directory, "export.zip");
        reject(() => AnemoneExportService.Save(path, project with { Resources = [project.Resources[0]] }), "export requires both backgrounds");
        check(!File.Exists(path), "failed export creates no archive");

        AnemoneExportService.Save(path, project);
        using (var zip = ZipFile.OpenRead(path))
        using (var metadata = new BinaryReader(zip.GetEntry("info.smdh")!.Open()))
        {
            check(metadata.ReadBytes(8).AsSpan().SequenceEqual("SMDH\0\0\0\0"u8) &&
                Encoding.Unicode.GetString(metadata.ReadBytes(0x80)).TrimEnd('\0') == project.Name,
                "SMDH signature, version and UTF-16 title");
        }

        var body = ReadBody(path, check);
        VerifyWithUsagi(path, project, check);
        check(body.Length == 0x800D0 && body[5] == 0 &&
            U32(body, 0) == 1 && U32(body, 0x0C) == 3 && U32(body, 0x10) == 1 &&
            U32(body, 0x18) == 0xD0 && U32(body, 0x20) == 3 && U32(body, 0x24) == 1 &&
            U32(body, 0x28) == 0x400D0 && body.AsSpan(0x2C, 0xD0 - 0x2C).IndexOfAnyExcept((byte)0) < 0,
            "version, static modes, disabled optional sections and aligned texture offsets");
        var topTexture = body.AsSpan(0xD0);
        check(U16(topTexture, 0) == 0xF800 && U16(topTexture, 40) == 0xF800 &&
            U16(topTexture, 42) == 0x07E0 && U16(topTexture, 128 + 4) == 0x001F &&
            U16(topTexture, 8192 + 40) == 0xFFFF, "RGB565 channels, Morton order and tile row orientation");
        check(U16(topTexture, 29 * 8192 + 51 * 128 + 2 * 47) == 0xFFE0 &&
            U16(topTexture, 29 * 8192 + 50 * 128 + 2 * 59) == 0xFFE0 &&
            U16(topTexture, 29 * 8192 + 51 * 128 + 2 * 58) == 0 &&
            U16(topTexture, 30 * 8192) == 0, "right eye margin and zero padding");
        var bottomTexture = body.AsSpan(0x400D0);
        check(U16(bottomTexture, 0) == 0x8410 && U16(bottomTexture, 2) == 0x001F &&
            U16(bottomTexture, 40 * 128) == 0, "saved crop, alpha compositing and bottom padding");

        var original = File.ReadAllBytes(path);
        try
        {
            AnemoneExportService.Save(path, project);
            check(false, "export refuses overwrite by default");
        }
        catch (IOException)
        {
            check(File.ReadAllBytes(path).SequenceEqual(original) &&
                !Directory.EnumerateFiles(directory, ".anemone-*.tmp").Any(), "unconfirmed overwrite preserves ZIP and cleans up");
        }

        AnemoneExportService.Save(path, project with
        {
            Resources = [project.Resources[0], project.Resources[1] with { Crop = new ImageCrop(1, -1, 0) }]
        }, overwrite: true);
        check(U16(ReadBody(path, check), 0x400D0) == 0xF800, "confirmed overwrite uses updated crop");
        File.Delete(path);
    }

    public static byte[] ReadBody(string path, Action<bool, string> check)
    {
        using var zip = ZipFile.OpenRead(path);
        check(zip.Entries.Select(entry => entry.FullName).SequenceEqual(["body_LZ.bin", "info.smdh"]) &&
            zip.Entries[1].Length == 0x36C0, "ZIP contains the root theme body and SMDH metadata");
        using var input = zip.Entries[0].Open();
        using var reader = new BinaryReader(input);
        uint header = reader.ReadUInt32();
        check((header & 255) == 0x11 && header >> 8 == 0x800D0 &&
            zip.Entries[0].Length <= 0x150000, "LZ11 magic, declared size and cache limit");

        // Read tokens from the documented format and reject invalid references or trailing bytes.
        var body = new byte[header >> 8];
        int position = 0;
        bool usedBackReference = false;
        while (position < body.Length)
        {
            int flags = reader.ReadByte();
            for (int mask = 128; mask != 0 && position < body.Length; mask >>= 1)
            {
                int first = reader.ReadByte();
                if ((flags & mask) == 0)
                {
                    body[position++] = (byte)first;
                    continue;
                }
                usedBackReference = true;
                int mode = first >> 4;
                int length = mode + 1;
                int highOffset = first & 15;
                if (mode == 0)
                {
                    int second = reader.ReadByte();
                    length = (highOffset << 4 | second >> 4) + 17;
                    highOffset = second & 15;
                }
                else if (mode == 1)
                {
                    int second = reader.ReadByte();
                    int third = reader.ReadByte();
                    length = (highOffset << 12 | second << 4 | third >> 4) + 273;
                    highOffset = third & 15;
                }
                int distance = (highOffset << 8 | reader.ReadByte()) + 1;
                if (distance > position || length > body.Length - position)
                    throw new InvalidDataException("Invalid LZ11 reference.");
                for (int i = 0; i < length; i++, position++)
                    body[position] = body[position - distance];
            }
        }
        var padding = reader.ReadBytes(4);
        check(usedBackReference && padding.Length < 4 && padding.All(value => value == 0) &&
            zip.Entries[0].Length % 4 == 0, "LZ11 references decode with valid padding");
        return body;
    }

    public static void VerifyWithUsagi(string path, ThemeProject project, Action<bool, string> check)
    {
#if VERIFY_WITH_USAGI
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("body_LZ.bin")!;
        using var input = entry.Open();
        using var output = new MemoryStream();
        ThemeEditor.Common.Compression.LZ11.Decompress(input, entry.Length, output);
        check(output.Length == 0x800D0, "independent Usagi LZ11 decoder");
        output.Position = 0;
        var theme = ThemeEditor.Common.Themes.Theme.Read(output);
        check(!theme.Flags.BackgroundMusic && (int)theme.Flags.TopFrameType == 1 &&
              (int)theme.Flags.BottomFrameType == 1, "independent Usagi theme reader");
        VerifyTexture(ThemeImportService.Top, theme.Textures.Top);
        VerifyTexture(ThemeImportService.Bottom, theme.Textures.Bottom);

        void VerifyTexture(string id, ThemeEditor.Common.Graphics.RawTexture texture)
        {
            var resource = project.Resources.Single(item => item.Id == id);
            var (width, height) = CropRenderer.ScreenSize(id)!.Value;
            using var source = SKBitmap.Decode(resource.Png);
            using var rendered = SKBitmap.Decode(CropRenderer.Render(source, width, height, resource.Crop ?? ImageCrop.Centered));
            using var opaque = new SKBitmap(width, height);
            using (var canvas = new SKCanvas(opaque))
            {
                canvas.Clear(SKColors.Black);
                canvas.DrawBitmap(rendered, 0, 0);
            }
            var decoded = texture.Decode();
            int leftMargin = id == ThemeImportService.Top ? 6 : 0;
            bool match = texture.Width == 512 && texture.Height == 256;
            for (int y = 0; y < texture.Height && match; y++)
            for (int x = 0; x < texture.Width && match; x++)
            {
                var color = x < width + 2 * leftMargin && y < height
                    ? opaque.GetPixel(Math.Clamp(x - leftMargin, 0, width - 1), y)
                    : SKColors.Black;
                int index = (y * texture.Width + x) * 3;
                match = (decoded[index] >> 3) == (color.Blue >> 3) &&
                        (decoded[index + 1] >> 2) == (color.Green >> 2) &&
                        (decoded[index + 2] >> 3) == (color.Red >> 3);
            }
            check(match, $"independent Usagi texture decoder ({id}, all pixels)");
        }
#endif
    }

    private static ThemeResource Resource(string id, SKBitmap bitmap, ImageCrop? crop = null)
    {
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return PngReader.Decode(id, id + ".png", png.ToArray()) with { Crop = crop };
    }

    private static uint U32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    private static ushort U16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
}
