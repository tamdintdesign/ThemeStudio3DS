using System.IO.Compression;
using System.Text;
using SkiaSharp;
using ThemeStudio3DS.Models;
using ThemeStudio3DS.Services;

using var pixels = new SKBitmap(400, 240);
pixels.Erase(SKColors.Coral);
using var encoded = pixels.Encode(SKEncodedImageFormat.Png, 100);
var png = encoded.ToArray();

using var nested = Pack(
    ("assets/TOP_SCREEN.PNG", png), ("details/folder_01.png", png),
    ("details/unmapped.png", png), ("README.txt", [42]));
var import = ThemeImportService.Read(nested);
Check(import.Resources.Select(item => item.Id).SequenceEqual(
    ["top_screen", "folder_01", "other/details/unmapped.png"]), "nested ZIP and unknown PNG");
Check(import.Report.Contains("Inconnu : README.txt") &&
      import.Report.Contains("Manquant : bottom_screen"), "unknown and missing files");

using var manifest = Pack(("data/custom.png", png), ("info/theme.json", Encoding.UTF8.GetBytes(
    """{"resources":{"bottom_screen":"data/custom.png"}}""")));
var explicitImport = ThemeImportService.Read(manifest);
Check(explicitImport.Resources.Single().Id == "bottom_screen" &&
    explicitImport.Report.Contains("320 × 240"), "manifest mapping and dimensions");

RejectPack("path traversal", ("../top_screen.png", png));
RejectPack("case-insensitive duplicate path", ("top_screen.png", png), ("TOP_SCREEN.PNG", png));
RejectPack("duplicate slot", ("a/top_screen.png", png), ("b/top_screen.png", png));
RejectPack("invalid PNG", ("top_screen.png", [1, 2, 3]));
RejectPack("missing manifest asset", ("theme.json", Encoding.UTF8.GetBytes(
    """{"resources":{"top_screen":"missing.png"}}""")));
RejectPack("entry count cap", Enumerable.Range(0, 101).Select(i => ($"{i}.txt", new byte[] { 0 })).ToArray());
using var overLimit = new MemoryStream([1, 2, 3]);
Reject(() => PngReader.ReadBounded(overLimit, 2), "stream byte cap");

var crop = new ImageCrop(2, 0.5, -0.5);
using var cropped = SKBitmap.Decode(CropRenderer.Render(pixels, 320, 240, crop));
Check(cropped.Width == 320 && cropped.Height == 240, "crop output dimensions");
using var stripes = new SKBitmap(640, 240);
using (var canvas = new SKCanvas(stripes))
{
    canvas.Clear(SKColors.Red);
    using var paint = new SKPaint { Color = SKColors.Blue };
    canvas.DrawRect(320, 0, 320, 240, paint);
}
using var left = SKBitmap.Decode(CropRenderer.Render(stripes, 320, 240, new ImageCrop(1, -1, 0)));
using var right = SKBitmap.Decode(CropRenderer.Render(stripes, 320, 240, new ImageCrop(1, 1, 0)));
Check(left.GetPixel(160, 120) == SKColors.Red && right.GetPixel(160, 120) == SKColors.Blue, "crop panning");
Reject(() => CropRenderer.Render(pixels, 320, 240, new ImageCrop(double.NaN, 0, 0)), "invalid crop");

var directory = Directory.CreateTempSubdirectory("theme-studio-tests-");
try
{
    ExportTests.Run(directory.FullName, Check, Reject);
    var path = Path.Combine(directory.FullName, "theme.3dstudio");
    var project = new ThemeProject("Thème", "Auteur", "#9BB5A3",
        [import.Resources[0] with { Crop = crop }, .. import.Resources.Skip(1)]);
    ThemeProjectService.Save(path, project);
    ThemeProject loaded;
    using (var stream = File.OpenRead(path))
        loaded = ThemeProjectService.Read(stream);
    Check(loaded.Name == project.Name && loaded.Author == project.Author && loaded.Accent == project.Accent,
        "metadata round trip");
    Check(loaded.Resources[0].Crop == crop && loaded.Resources.Count == 3 &&
          loaded.Resources.Select(resource => resource.Id).SequenceEqual(project.Resources.Select(resource => resource.Id)) &&
          loaded.Resources.Zip(project.Resources).All(pair => pair.First.Png.SequenceEqual(pair.Second.Png)),
        "sources and crop round trip");
    var written = File.ReadAllBytes(path);
    Reject(() => ThemeProjectService.Save(path, project with { Accent = "invalid" }), "invalid project rejected");
    Check(File.ReadAllBytes(path).SequenceEqual(written), "failed save preserves previous project");
    ThemeProjectService.Save(path, project with { Name = "Révision" });
    using (var stream = File.OpenRead(path))
        Check(ThemeProjectService.Read(stream).Name == "Révision", "atomic project replacement");
    Check(Directory.GetFiles(directory.FullName).Length == 1, "no save temporary files");
}
finally
{
    directory.Delete(recursive: true);
}

using var wrongVersion = Pack(("project.json", Encoding.UTF8.GetBytes(
    """{"version":42,"images":[]}""")));
Reject(() => ThemeProjectService.Read(wrongVersion), "unsupported project version");

if (args.Length is 1 or 3)
{
    using var input = File.OpenRead(args[0]);
    var knownPack = ThemeImportService.Read(input);
    Check(knownPack.Resources.Count == 10 && knownPack.Resources.Count(resource =>
        resource.Id.StartsWith("folder_", StringComparison.Ordinal)) == 5 &&
        knownPack.Resources.Any(resource => resource.Id == ThemeImportService.Thumbnail) &&
        knownPack.Report.Contains("Inconnu : README.txt"), "real Frieren pack");
    var frierenDirectory = Directory.CreateTempSubdirectory("frieren-export-");
    try
    {
        string projectPath = Path.Combine(frierenDirectory.FullName, "Frieren Minimal.3dstudio");
        ThemeProjectService.Save(projectPath, new ThemeProject("Frieren Minimal", "", "#849B82", knownPack.Resources));
        using var saved = File.OpenRead(projectPath);
        var reopened = ThemeProjectService.Read(saved);
        string zipPath = Path.Combine(frierenDirectory.FullName, "Frieren Minimal.zip");
        AnemoneExportService.Save(zipPath, reopened);
        Check(ExportTests.ReadBody(zipPath, Check).Length == 0x800D0 &&
            reopened.Resources.Zip(knownPack.Resources).All(pair => pair.First.Png.SequenceEqual(pair.Second.Png)),
            "Frieren ZIP import, saved project reopen, export and original PNG preservation");
        ExportTests.VerifyWithUsagi(zipPath, reopened, Check);

        if (args.Length == 3)
        {
            using var existingFile = File.OpenRead(args[1]);
            var existing = ThemeProjectService.Read(existingFile);
            Check(existing.Resources.Count == knownPack.Resources.Count &&
                  existing.Resources.All(resource => knownPack.Resources.Any(imported =>
                      imported.Id == resource.Id && imported.Png.SequenceEqual(resource.Png))),
                "existing Frieren project matches the pack byte for byte");
            if (!File.Exists(args[2]))
                AnemoneExportService.Save(args[2], existing);
            ExportTests.ReadBody(args[2], Check);
            ExportTests.VerifyWithUsagi(args[2], existing, Check);
        }
    }
    finally
    {
        frierenDirectory.Delete(recursive: true);
    }
}

static MemoryStream Pack(params (string Name, byte[] Bytes)[] entries)
{
    var stream = new MemoryStream();
    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var (name, bytes) in entries)
        {
            using var output = archive.CreateEntry(name).Open();
            output.Write(bytes);
        }
    }
    stream.Position = 0;
    return stream;
}

static void RejectPack(string label, params (string Name, byte[] Bytes)[] entries)
{
    using var stream = Pack(entries);
    Reject(() => ThemeImportService.Read(stream), label);
}

static void Reject(Action action, string label)
{
    try
    {
        action();
    }
    catch (InvalidDataException)
    {
        Check(true, label);
        return;
    }
    Check(false, label);
}

static void Check(bool condition, string label)
{
    if (!condition)
        throw new InvalidOperationException($"FAIL {label}");
    Console.WriteLine($"PASS {label}");
}
