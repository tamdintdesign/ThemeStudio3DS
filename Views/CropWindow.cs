using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using ThemeStudio3DS.Models;
using ThemeStudio3DS.Services;

namespace ThemeStudio3DS.Views;

internal sealed class CropWindow : Window
{
    private readonly SKBitmap source;
    private readonly (int Width, int Height) size;
    private readonly Image preview = new() { Stretch = Stretch.Uniform };
    private readonly Slider zoom = new() { Minimum = 1, Maximum = 4, SmallChange = 0.05 };
    private readonly Slider horizontal = new() { Minimum = -1, Maximum = 1, SmallChange = 0.05 };
    private readonly Slider vertical = new() { Minimum = -1, Maximum = 1, SmallChange = 0.05 };

    public CropWindow(ThemeResource resource, (int Width, int Height) size)
    {
        this.size = size;
        source = SKBitmap.Decode(resource.Png) ?? throw new InvalidDataException("Image source illisible.");

        Title = "Recadrer le fond";
        Width = 530;
        Height = 550;
        MinWidth = 360;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var reset = new Button { Content = "Réinitialiser" };
        reset.Click += (_, _) => SetCrop(ImageCrop.Centered);
        var cancel = new Button { Content = "Annuler", IsCancel = true };
        cancel.Click += (_, _) => Close(null);
        var apply = new Button { Content = "Appliquer", IsDefault = true };
        apply.Click += (_, _) => Close(CurrentCrop);

        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(reset);
        actions.Children.Add(cancel);
        actions.Children.Add(apply);
        foreach (var button in actions.Children)
            button.Margin = new Thickness(0, 0, 8, 4);

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 7,
                Children =
                {
                    new TextBlock { Text = $"Résultat · {size.Width} × {size.Height}" },
                    new Border { Height = 220, Background = Brushes.Black, Child = preview },
                    new TextBlock { Text = "Zoom" }, zoom,
                    new TextBlock { Text = "Position horizontale" }, horizontal,
                    new TextBlock { Text = "Position verticale" }, vertical,
                    actions
                }
            }
        };
        SetCrop(resource.Crop ?? ImageCrop.Centered);
        zoom.ValueChanged += UpdatePreview;
        horizontal.ValueChanged += UpdatePreview;
        vertical.ValueChanged += UpdatePreview;
        Closed += (_, _) =>
        {
            (preview.Source as Bitmap)?.Dispose();
            source.Dispose();
        };
    }

    private ImageCrop CurrentCrop => new(zoom.Value, horizontal.Value, vertical.Value);

    private void SetCrop(ImageCrop crop)
    {
        zoom.Value = crop.Zoom;
        horizontal.Value = crop.X;
        vertical.Value = crop.Y;
        Refresh();
    }

    private void UpdatePreview(object? sender, RangeBaseValueChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        using var stream = new MemoryStream(CropRenderer.Render(source, size.Width, size.Height, CurrentCrop));
        var previous = preview.Source as Bitmap;
        preview.Source = new Bitmap(stream);
        previous?.Dispose();
    }
}
