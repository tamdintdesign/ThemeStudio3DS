namespace ThemeStudio3DS.Models;

public sealed record ThemeResource(string Id, string FileName, byte[] Png, int Width, int Height, ImageCrop? Crop = null);
