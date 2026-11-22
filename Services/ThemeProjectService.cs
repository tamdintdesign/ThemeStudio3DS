using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using ThemeStudio3DS.Models;

namespace ThemeStudio3DS.Services;

public static class ThemeProjectService
{
    private const string ManifestName = "project.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private sealed record Manifest(int Version, string Name, string Author, string Accent, ManifestImage[] Images);
    private sealed record ManifestImage(string Id, string FileName, string Path, ImageCrop? Crop);

    public static ThemeProject Read(Stream input)
    {
        using var archive = new ThemeArchive(input);
        if (!archive.Files.TryGetValue(ManifestName, out var entry))
            throw new InvalidDataException("Le manifeste project.json est absent.");

        var manifest = JsonSerializer.Deserialize<Manifest>(
            ThemeArchive.Read(entry, ThemeArchive.MaxManifestBytes), Json);
        if (manifest is not { Version: 1, Images: not null })
            throw new InvalidDataException("Version de projet inconnue ou manifeste incomplet.");

        ValidateMetadata(manifest.Name, manifest.Author, manifest.Accent);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ManifestName };
        var resources = new List<ThemeResource>();
        foreach (var image in manifest.Images)
        {
            if (image is null || !ValidLabel(image.Id, 2048) || !ValidLabel(image.FileName, 2048) ||
                image.Path is null || !image.Path.StartsWith("images/", StringComparison.Ordinal) ||
                !paths.Add(image.Path) || !ids.Add(image.Id) || !archive.Files.TryGetValue(image.Path, out var png))
                throw new InvalidDataException("Référence d’image invalide ou en double.");

            ValidateCrop(image.Id, image.Crop);
            var resource = PngReader.Decode(image.Id, image.FileName, ThemeArchive.Read(png, PngReader.MaxBytes));
            resources.Add(resource with { Crop = image.Crop });
        }

        if (paths.Count != archive.Files.Count)
            throw new InvalidDataException("Le projet contient des fichiers absents du manifeste.");

        return new ThemeProject(manifest.Name, manifest.Author, manifest.Accent, resources);
    }

    public static void Save(string path, ThemeProject project)
    {
        ValidateMetadata(project.Name, project.Author, project.Accent);
        if (project.Resources.Count >= ThemeArchive.MaxEntries ||
            project.Resources.Sum(resource => (long)resource.Png.Length) > ThemeArchive.MaxTotalBytes)
            throw new InvalidDataException("Le projet dépasse les limites d’archive.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var images = new List<ManifestImage>();
        foreach (var resource in project.Resources)
        {
            if (!ValidLabel(resource.Id, 2048) || !ValidLabel(resource.FileName, 2048) ||
                !ids.Add(resource.Id) || resource.Png.Length > PngReader.MaxBytes)
                throw new InvalidDataException("Ressource invalide ou en double.");
            ValidateCrop(resource.Id, resource.Crop);
            images.Add(new ManifestImage(resource.Id, resource.FileName, $"images/{images.Count:D3}.png", resource.Crop));
        }

        var manifest = new Manifest(1, project.Name, project.Author, project.Accent, images.ToArray());
        var json = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
        if (json.Length > ThemeArchive.MaxManifestBytes ||
            project.Resources.Sum(resource => (long)resource.Png.Length) + json.Length > ThemeArchive.MaxTotalBytes)
            throw new InvalidDataException("Le manifeste ou le projet est trop volumineux.");

        var target = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".3dstudio-{Guid.NewGuid():N}.tmp");
        var created = false;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    Write(archive, ManifestName, json);
                    for (int i = 0; i < images.Count; i++)
                        Write(archive, images[i].Path, project.Resources[i].Png);
                }
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (created && File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void Write(ZipArchive archive, string path, byte[] bytes)
    {
        using var stream = archive.CreateEntry(path, CompressionLevel.NoCompression).Open();
        stream.Write(bytes);
    }

    private static bool ValidLabel(string? value, int limit) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= limit && !value.Any(char.IsControl);

    private static void ValidateCrop(string id, ImageCrop? crop)
    {
        if (crop is not null && (!crop.IsValid || CropRenderer.ScreenSize(id) is null))
            throw new InvalidDataException("Paramètres de recadrage invalides.");
    }

    private static void ValidateMetadata(string? name, string? author, string? accent)
    {
        if (name is null || name.Length > 150 || author is null || author.Length > 150 ||
            accent is null || accent.Length != 7 || accent[0] != '#' || !accent.Skip(1).All(Uri.IsHexDigit))
            throw new InvalidDataException("Nom, auteur ou couleur invalide. La couleur doit avoir le format #RRGGBB.");
    }
}
