using System.Text.Json.Serialization;

namespace ThemeStudio3DS.Models;

public sealed record ImageCrop(double Zoom, double X, double Y)
{
    public static ImageCrop Centered { get; } = new(1, 0, 0);
    [JsonIgnore]
    public bool IsValid => Zoom is >= 1 and <= 4 && X is >= -1 and <= 1 && Y is >= -1 and <= 1;
}
