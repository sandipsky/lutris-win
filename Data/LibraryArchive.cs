using System;
using System.IO;
using System.IO.Compression;

namespace Lutris.Data;

/// <summary>
/// Reads and writes the portable library archive: a zip holding <c>library.db</c> at its root
/// and every banner under <c>banners/</c>. The format is shared with the Electron version.
/// </summary>
public static class LibraryArchive
{
    public const string DbEntryName = "library.db";
    private const string BannersPrefix = "banners/";

    public static void Export(string dbPath, string bannersDir, string destinationZip)
    {
        if (File.Exists(destinationZip)) File.Delete(destinationZip);

        using var zip = ZipFile.Open(destinationZip, ZipArchiveMode.Create);
        if (File.Exists(dbPath))
        {
            zip.CreateEntryFromFile(dbPath, DbEntryName, CompressionLevel.Optimal);
        }
        if (Directory.Exists(bannersDir))
        {
            foreach (var file in Directory.EnumerateFiles(bannersDir))
            {
                // Images are already compressed; storing them keeps export fast.
                zip.CreateEntryFromFile(file, BannersPrefix + Path.GetFileName(file), CompressionLevel.NoCompression);
            }
        }
    }

    public static bool ContainsLibrary(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.GetEntry(DbEntryName) is not null;
    }

    public static void ExtractInto(string zipPath, string dbPath, string bannersDir)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var dbEntry = zip.GetEntry(DbEntryName)
            ?? throw new InvalidDataException("The archive does not contain a library.db file.");

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        dbEntry.ExtractToFile(dbPath, overwrite: true);

        Directory.CreateDirectory(bannersDir);
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (!name.StartsWith(BannersPrefix, StringComparison.OrdinalIgnoreCase)) continue;

            // Only the file name is used, so a crafted archive cannot escape the banners folder.
            var fileName = Path.GetFileName(name);
            if (fileName.Length == 0) continue;
            entry.ExtractToFile(Path.Combine(bannersDir, fileName), overwrite: true);
        }
    }
}
