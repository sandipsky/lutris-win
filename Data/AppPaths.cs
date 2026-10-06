using System;
using System.IO;

namespace Lutris.Data;

/// <summary>
/// Locations of the library database and banner images on disk.
/// Set the LUTRIS_DATA_DIR environment variable to relocate everything (portable installs, tests).
/// </summary>
public static class AppPaths
{
    public static string DataDir { get; } =
        Environment.GetEnvironmentVariable("LUTRIS_DATA_DIR") is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lutris");

    public static string DbPath => Path.Combine(DataDir, "library.db");

    public static string BannersDir => Path.Combine(DataDir, "banners");

    public static string LogPath => Path.Combine(DataDir, "lutris.log");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(BannersDir);
    }
}
