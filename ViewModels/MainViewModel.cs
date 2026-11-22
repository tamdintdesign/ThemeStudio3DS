using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThemeStudio3DS.Models;
using ThemeStudio3DS.Services;

namespace ThemeStudio3DS.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private static readonly string[] DefaultSlots =
    [
        ThemeImportService.Top, ThemeImportService.Bottom,
        ThemeImportService.TopCanvas, ThemeImportService.BottomCanvas, ThemeImportService.Thumbnail
    ];

    [ObservableProperty] public partial IReadOnlyList<ResourceItem> Items { get; set; } =
        DefaultSlots.Select(id => new ResourceItem(id, null)).ToArray();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TopEmpty))]
    public partial Bitmap? TopPreview { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BottomEmpty))]
    public partial Bitmap? BottomPreview { get; set; }
    public bool TopEmpty => TopPreview is null;
    public bool BottomEmpty => BottomPreview is null;

    [ObservableProperty] public partial bool ShowingImages { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string Report { get; set; } = "";
    [ObservableProperty] public partial string Status { get; set; } = "Prêt";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    public partial string Name { get; set; } = "Sans titre";

    [ObservableProperty] public partial string Author { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccentBrush))]
    public partial string Accent { get; set; } = "#849B82";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyPropertyChangedFor(nameof(ProjectState))]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectState))]
    public partial string? ProjectPath { get; set; }

    public IBrush AccentBrush => Color.TryParse(Accent, out var color) ? new SolidColorBrush(color) : Brushes.Transparent;

    public string WindowTitle => $"3DS Theme Studio · {Name}{(IsDirty ? " *" : "")}";
    public string ProjectState => IsDirty ? "Modifications non enregistrées" :
        ProjectPath is null ? "Nouveau projet" : Path.GetFileName(ProjectPath);

    partial void OnNameChanged(string value) => IsDirty = true;
    partial void OnAuthorChanged(string value) => IsDirty = true;
    partial void OnAccentChanged(string value) => IsDirty = true;

    public void LoadProject(ThemeProject project, string? path)
    {
        ApplyResources(project.Resources);
        Name = project.Name;
        Author = project.Author;
        Accent = project.Accent;
        ProjectPath = path;
        IsDirty = false;
        Report = "";
        Status = path is null ? "Nouveau projet" : "Projet ouvert";
    }

    private ThemeProject Snapshot() => new(Name, Author, Accent, Resources.ToArray());

    public Task ExportAnemoneAsync(string path, bool overwrite)
    {
        var snapshot = Snapshot();
        return Task.Run(() => AnemoneExportService.Save(path, snapshot, overwrite));
    }

    public Task<ThemeProject> ReadProjectAsync(Stream stream) => Task.Run(() => ThemeProjectService.Read(stream));

    public async Task SaveProjectAsync(string path)
    {
        var snapshot = Snapshot();
        await Task.Run(() => ThemeProjectService.Save(path, snapshot));
        ProjectPath = path;
        IsDirty = false;
        Status = "Projet enregistré";
    }

    public IEnumerable<ThemeResource> Resources => Items.Select(item => item.Resource).OfType<ThemeResource>();

    [RelayCommand]
    private void ShowImages() => ShowingImages = true;

    [RelayCommand]
    private void ShowScreens() => ShowingImages = false;

    public async Task ImportImageAsync(string id, string name, Stream stream)
    {
        var image = await Task.Run(() => PngReader.Read(id, name, stream));
        ApplyResources(Resources.Where(resource => !resource.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).Append(image));
        Status = $"Image importée · {image.Width} × {image.Height}";
        if (ThemeImportService.ExpectedSize(id) is { } size && size != (image.Width, image.Height))
            Status += $" · dimensions attendues {size.Width} × {size.Height}";
    }

    public Task<ThemeImport> ReadPackAsync(Stream stream) => Task.Run(() => ThemeImportService.Read(stream));

    public void ApplyCrop(string id, ImageCrop crop)
    {
        ApplyResources(Resources.Select(resource => resource.Id == id ? resource with { Crop = crop } : resource));
        Status = "Recadrage appliqué";
    }

    public void ApplyResources(IEnumerable<ThemeResource> resources)
    {
        var byId = resources.ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);
        var updated = new List<ResourceItem>();
        try
        {
            foreach (var id in DefaultSlots.Concat(byId.Keys.Order(StringComparer.OrdinalIgnoreCase))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var resource = byId.GetValueOrDefault(id);
                var existing = Items.FirstOrDefault(item => item.Id == id && ReferenceEquals(item.Resource, resource));
                updated.Add(existing ?? new ResourceItem(id, resource));
            }
        }
        catch
        {
            foreach (var item in updated)
                if (!Items.Contains(item))
                    item.Dispose();
            throw;
        }

        var previous = Items;
        Items = updated;
        TopPreview = updated[0].Preview;
        BottomPreview = updated[1].Preview;
        IsDirty = true;
        foreach (var item in previous)
            if (!updated.Contains(item))
                item.Dispose();
    }

    public void Dispose()
    {
        foreach (var item in Items)
            item.Dispose();
    }
}
