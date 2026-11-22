using System;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using Avalonia.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ThemeStudio3DS.Models;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ThemeStudio3DS.Services;
using ThemeStudio3DS.ViewModels;

namespace ThemeStudio3DS.Views;

public partial class MainWindow : Window
{
    private MainViewModel Model => (MainViewModel)DataContext!;
    private static readonly FilePickerFileType Png = new("Image PNG") { Patterns = ["*.png"] };
    private static readonly FilePickerFileType Zip = new("Archive ZIP") { Patterns = ["*.zip"] };

    private static readonly FilePickerFileType Project = new("Projet 3DS Theme Studio") { Patterns = ["*.3dstudio"] };
    private bool closingApproved;

    public MainWindow()
    {
        InitializeComponent();
        Closing += ConfirmClosing;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Closed += (_, _) => Model.Dispose();
    }

    private async void New_Click(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (await ConfirmDiscardAsync())
            Model.LoadProject(new ThemeProject("Sans titre", "", "#849B82", []), null);
    });

    private async void Open_Click(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var file = await PickAsync("Ouvrir un projet", Project);
        if (file is null)
            return;
        await using var stream = await file.OpenReadAsync();
        var project = await Model.ReadProjectAsync(stream);
        if (await ConfirmDiscardAsync())
            Model.LoadProject(project, file.TryGetLocalPath());
    });

    private async void Save_Click(object? sender, RoutedEventArgs e) => await SaveAsync(false);
    private async void SaveAs_Click(object? sender, RoutedEventArgs e) => await SaveAsync(true);

    private Task SaveAsync(bool saveAs) => RunAsync(async () => await SaveCoreAsync(saveAs));

    private async Task<bool> SaveCoreAsync(bool saveAs)
    {
        var path = saveAs ? null : Model.ProjectPath;
        if (path is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Enregistrer le projet",
                SuggestedFileName = "theme.3dstudio",
                DefaultExtension = "3dstudio",
                FileTypeChoices = [Project],
                ShowOverwritePrompt = true
            });
            if (file is null)
                return false;
            path = file.TryGetLocalPath() ?? throw new System.IO.IOException("Choisissez un dossier local.");
        }
        await Model.SaveProjectAsync(path);
        return true;
    }

    private async void Export_Click(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exporter pour Anemone3DS",
            SuggestedFileName = "theme.zip",
            DefaultExtension = "zip",
            FileTypeChoices = [Zip],
            ShowOverwritePrompt = false
        });
        if (file is null)
            return;

        var path = file.TryGetLocalPath() ?? throw new IOException("Choisissez un dossier local pour le ZIP.");
        bool overwrite = File.Exists(path);
        if (overwrite && !await ShowMessageAsync("Remplacer le ZIP", $"Remplacer « {Path.GetFileName(path)} » ?", "Remplacer"))
            return;

        try
        {
            await Model.ExportAnemoneAsync(path, overwrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Impossible d’enregistrer le ZIP « {Path.GetFileName(path)} ». {ex.Message}", ex);
        }

        Model.Status = $"ZIP Anemone3DS exporté · {Path.GetFileName(path)}";
    });

    private Task<bool> ConfirmDiscardAsync() => !Model.IsDirty ? Task.FromResult(true) :
        ShowMessageAsync("Projet modifié", "Abandonner les modifications non enregistrées ?", "Abandonner");

    private async void ConfirmClosing(object? sender, WindowClosingEventArgs e)
    {
        if (closingApproved)
            return;

        e.Cancel = Model.IsBusy || Model.IsDirty;
        if (!Model.IsBusy && Model.IsDirty)
        {
            await RunAsync(async () => closingApproved = await ConfirmDiscardAsync());
            if (closingApproved)
                Close();
        }
    }

    private async void ImportTop_Click(object? sender, RoutedEventArgs e) =>
        await ImportImageAsync(ThemeImportService.Top);

    private async void ImportBottom_Click(object? sender, RoutedEventArgs e) =>
        await ImportImageAsync(ThemeImportService.Bottom);

    private async void CropTop_Click(object? sender, RoutedEventArgs e) =>
        await CropAsync(ThemeImportService.Top);

    private async void CropBottom_Click(object? sender, RoutedEventArgs e) =>
        await CropAsync(ThemeImportService.Bottom);

    private Task CropAsync(string id) => RunAsync(async () =>
    {
        var resource = Model.Resources.FirstOrDefault(item => item.Id == id);
        if (resource is null || CropRenderer.ScreenSize(id) is not { } size)
            return;
        var crop = await new CropWindow(resource, size).ShowDialog<ImageCrop?>(this);
        if (crop is not null)
            Model.ApplyCrop(id, crop);
    });

    private async void Replace_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ResourceItem item })
            await ImportImageAsync(item.Id);
    }

    private Task ImportImageAsync(string id) => RunAsync(async () =>
    {
        var file = await PickAsync("Choisir une image PNG", Png);
        if (file is null)
            return;
        await using var stream = await file.OpenReadAsync();
        await Model.ImportImageAsync(id, file.Name, stream);
    });

    private async void ImportZip_Click(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var file = await PickAsync("Importer un pack de thème 3DS", Zip);
        if (file is null)
            return;

        await ImportPackAsync(file);
    });

    private static IStorageFile? DroppedFile(DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is [IStorageFile file] &&
            Path.GetExtension(file.Name).ToLowerInvariant() is ".png" or ".zip")
            return file;
        return null;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = !Model.IsBusy && DroppedFile(e) is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        var file = DroppedFile(e);
        if (file is null)
            return;

        await RunAsync(async () =>
        {
            if (Path.GetExtension(file.Name).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                await ImportPackAsync(file);
                return;
            }

            var id = ThemeImportService.Recognize(file.Name) ?? $"other/{file.Name}";
            if (Model.Resources.Any(resource => resource.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) &&
                !await ShowMessageAsync("Remplacer l’image", $"Remplacer la ressource par {file.Name} ?", "Remplacer"))
                return;

            await using var input = await file.OpenReadAsync();
            await Model.ImportImageAsync(id, file.Name, input);
            Model.ShowImagesCommand.Execute(null);
        });
    }

    private async Task ImportPackAsync(IStorageFile file)
    {
        await using var stream = await file.OpenReadAsync();
        var import = await Model.ReadPackAsync(stream);
        if (Model.Resources.Any() &&
            !await ShowMessageAsync("Importer le pack", "Remplacer les ressources du projet courant par celles du pack ?", "Remplacer"))
            return;

        Model.ApplyResources(import.Resources);
        Model.Report = import.Report;
        Model.Status = $"{import.Resources.Count} images importées";
    }

    private async Task<IStorageFile?> PickAsync(string title, FilePickerFileType type)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            FileTypeFilter = [type]
        });
        return files.Count == 0 ? null : files[0];
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (Model.IsBusy)
            return;

        Model.IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Model.Status = "Opération interrompue";
            await ShowMessageAsync("Erreur", ex.Message);
        }
        finally
        {
            Model.IsBusy = false;
        }
    }

    private Task<bool> ShowMessageAsync(string title, string message, string? confirm = null)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        if (confirm is not null)
        {
            var accept = new Button { Content = confirm };
            accept.Click += (_, _) => dialog.Close(true);
            buttons.Children.Add(accept);
        }
        var cancel = new Button { Content = confirm is null ? "Fermer" : "Annuler", IsCancel = true };
        cancel.Click += (_, _) => dialog.Close(false);
        buttons.Children.Add(cancel);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 16,
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, buttons }
        };
        return dialog.ShowDialog<bool>(this);
    }
}
