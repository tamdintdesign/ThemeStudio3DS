# 3DS Theme Studio

[Documentation en français](README.fr.md)

A free desktop editor for making static Nintendo 3DS HOME Menu themes on Linux.
Import two PNG backgrounds, adjust their crops, save an editable project, and
export an Anemone3DS theme ZIP.

## Run from source

Install the .NET 10 SDK and run these commands from the repository root:

```sh
dotnet build ThemeStudio3DS.csproj
dotnet run --project ThemeStudio3DS.csproj
dotnet run --project Tests/ThemeStudio3DS.Tests.csproj
```

The application uses Avalonia and has been exercised on Omarchy (Arch Linux).
Its current interface is in French.

## Make and test a theme

1. In **Écrans**, import the top and bottom backgrounds. The visible layouts
   are 400 × 240 and 320 × 240 pixels. **Recadrer** adjusts the crop, and
   **Ctrl+S** saves a `.3dstudio` project with the original PNGs.
2. Choose **Exporter pour Anemone3DS** (**Ctrl+E**) to create the theme ZIP.
3. Copy the ZIP to `/Themes/` on your 3DS SD card. Select it in Anemone3DS and
   use the normal single-theme install. See the
   [Anemone3DS usage guide](https://github.com/astronautlevel2/Anemone3DS/wiki/Usage).

The exporter produces a LZ11-encoded `body_LZ.bin` and a minimal `info.smdh`.
This beta supports two static backgrounds. Music, animations, custom HOME Menu
icons, and custom UI colors are outside its export scope. Usagi independently
decoded the exported LZ11 stream and both RGB565 textures. Hardware validation
is pending. See [format details](docs/anemone-format.md).

## License

[MIT](LICENSE). See [third-party notices](THIRD-PARTY-NOTICES.md).
