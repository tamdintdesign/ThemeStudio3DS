using System;
using System.IO;
using Avalonia.Media.Imaging;
using SkiaSharp;
using ThemeStudio3DS.Models;
using ThemeStudio3DS.Services;

namespace ThemeStudio3DS.ViewModels;

public sealed class ResourceItem : IDisposable
{
    public string Id { get; }
    public ThemeResource? Resource { get; }
    public Bitmap? Preview { get; }
    public bool IsEmpty => Resource is null;
    public string Label => Id switch
    {
        ThemeImportService.Top => "Fond supérieur",
        ThemeImportService.Bottom => "Fond inférieur",
        ThemeImportService.TopCanvas => "Texture source supérieure",
        ThemeImportService.BottomCanvas => "Texture source inférieure",
        ThemeImportService.Thumbnail => "Miniature du thème",
        _ when Id.StartsWith("folder_", StringComparison.Ordinal) => $"Icône de dossier {Id[7..]}",
        _ => Resource?.FileName ?? Id
    };
    public string Details => Resource is null ? "Aucune image" :
        $"{Resource.FileName} · {Resource.Width} × {Resource.Height}";
    public string Action => IsEmpty ? "Importer" : "Remplacer";

    public ResourceItem(string id, ThemeResource? resource)
    {
        Id = id;
        Resource = resource;
        if (resource is null)
            return;

        if (resource.Crop is { } crop && CropRenderer.ScreenSize(id) is { } size)
        {
            using var source = SKBitmap.Decode(resource.Png);
            using var cropped = new MemoryStream(CropRenderer.Render(source, size.Width, size.Height, crop));
            Preview = new Bitmap(cropped);
            return;
        }

        using var stream = new MemoryStream(resource.Png);
        int width = id is ThemeImportService.Top or ThemeImportService.Bottom ? 800 : 180;
        Preview = Bitmap.DecodeToWidth(stream, Math.Min(resource.Width, width));
    }

    public void Dispose() => Preview?.Dispose();
}
