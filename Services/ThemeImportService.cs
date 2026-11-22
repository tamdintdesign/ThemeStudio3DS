using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using ThemeStudio3DS.Models;

namespace ThemeStudio3DS.Services;

public sealed record ThemeImport(IReadOnlyList<ThemeResource> Resources, string Report);

public static partial class ThemeImportService
{
    public const string Top = "top_screen";
    public const string Bottom = "bottom_screen";
    public const string TopCanvas = "top_canvas_512x256";
    public const string BottomCanvas = "bottom_canvas_512x256";
    public const string Thumbnail = "thumbnail";

    [GeneratedRegex("^folder_[0-9]+$")]
    private static partial Regex FolderName();

    public static string? Recognize(string path)
    {
        if (!Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
            return null;

        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        return name switch
        {
            Top or Bottom or TopCanvas or BottomCanvas or Thumbnail => name,
            "preview" or "icon" => Thumbnail,
            _ => FolderName().IsMatch(name) ? name : null
        };
    }

    public static (int Width, int Height)? ExpectedSize(string id) => id switch
    {
        Top => (400, 240),
        Bottom => (320, 240),
        TopCanvas or BottomCanvas => (512, 256),
        _ => null
    };

    public static ThemeImport Read(Stream stream)
    {
        using var archive = new ThemeArchive(stream);
        var manifests = archive.Files.Keys.Where(path =>
            Path.GetFileName(path).Equals("theme.json", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (manifests.Length > 1)
            throw new InvalidDataException("Plusieurs manifestes theme.json.");

        var explicitIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (manifests.Length == 1)
        {
            var bytes = ThemeArchive.Read(archive.Files[manifests[0]], ThemeArchive.MaxManifestBytes);
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("resources", out var mapping) ||
                mapping.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Le manifeste doit contenir un objet resources.");

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in mapping.EnumerateObject())
            {
                var id = property.Name;
                var path = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Any(char.IsControl) ||
                    path is null || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    !archive.Files.ContainsKey(path) || !ids.Add(id) || !explicitIds.TryAdd(path, id.ToLowerInvariant()))
                    throw new InvalidDataException($"Ressource de manifeste invalide ou en double : {id}.");
            }
        }

        var resources = new List<ThemeResource>();
        var notes = new List<string>();
        var assigned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, entry) in archive.Files)
        {
            if (manifests.Contains(path))
                continue;

            var id = explicitIds.GetValueOrDefault(path) ?? Recognize(path);
            if (id is null)
            {
                notes.Add($"Inconnu : {path}");
                if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    continue;
                id = $"other/{path}";
            }

            if (!assigned.TryAdd(id, path))
                throw new InvalidDataException($"Conflit pour {id} : {assigned[id]} et {path}.");

            var image = PngReader.Decode(id, entry.Name, ThemeArchive.Read(entry, PngReader.MaxBytes));
            resources.Add(image);
            notes.Add($"Importé : {path} ({image.Width} × {image.Height})");
            if (ExpectedSize(id) is { } size && size != (image.Width, image.Height))
                notes.Add($"Dimensions attendues pour {id} : {size.Width} × {size.Height}");
        }

        foreach (var id in new[] { Top, Bottom, TopCanvas, BottomCanvas, Thumbnail })
            if (!assigned.ContainsKey(id))
                notes.Add($"Manquant : {id}");

        if (resources.Count == 0)
            throw new InvalidDataException("Aucune image PNG dans l’archive.");

        return new ThemeImport(resources, string.Join(Environment.NewLine, notes));
    }
}
