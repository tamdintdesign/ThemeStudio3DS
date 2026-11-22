using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ThemeStudio3DS.Services;

internal sealed class ThemeArchive : IDisposable
{
    public const int MaxEntries = 100;
    public const int MaxTotalBytes = 100 * 1024 * 1024;
    public const int MaxManifestBytes = 256 * 1024;

    private readonly MemoryStream buffer;
    private readonly ZipArchive archive;
    public Dictionary<string, ZipArchiveEntry> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ThemeArchive(Stream input)
    {
        buffer = new MemoryStream(PngReader.ReadBounded(input, MaxTotalBytes + 1024 * 1024));
        try
        {
            archive = new ZipArchive(buffer, ZipArchiveMode.Read);
            if (archive.Entries.Count > MaxEntries)
                throw new InvalidDataException("Trop de fichiers dans l’archive.");

            long total = 0;
            foreach (var entry in archive.Entries)
            {
                var path = entry.FullName.Replace('\\', '/');
                bool directory = path.EndsWith('/');
                if (path.Length > 512 || path.TrimEnd('/').Split('/').Any(s => s is "" or "." or ".." ||
                    s.IndexOfAny([':', '\0']) >= 0) || path.StartsWith('/') ||
                    (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                    throw new InvalidDataException($"Chemin ZIP interdit : {path}.");

                total += entry.Length;
                if (entry.Length > PngReader.MaxBytes || total > MaxTotalBytes)
                    throw new InvalidDataException("Archive décompressée trop volumineuse.");

                if (!directory && !Files.TryAdd(path, entry))
                    throw new InvalidDataException($"Chemin ZIP en double : {path}.");
            }
        }
        catch
        {
            archive?.Dispose();
            buffer.Dispose();
            throw;
        }
    }

    public static byte[] Read(ZipArchiveEntry entry, int limit)
    {
        using var input = entry.Open();
        var bytes = PngReader.ReadBounded(input, Math.Min(limit, (int)entry.Length));
        if (bytes.Length != entry.Length)
            throw new InvalidDataException($"Taille ZIP incohérente : {entry.FullName}.");
        return bytes;
    }

    public void Dispose()
    {
        archive.Dispose();
        buffer.Dispose();
    }
}
