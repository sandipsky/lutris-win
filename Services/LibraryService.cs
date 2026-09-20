using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lutris.Data;
using Lutris.Models;

namespace Lutris.Services;

/// <summary>
/// The single entry point the UI uses for everything stored on disk: games, banners and
/// the import/export archive. Must be used from the UI thread.
/// </summary>
public sealed class LibraryService
{
    private readonly LibraryDatabase _db;

    public LibraryService()
    {
        AppPaths.EnsureCreated();
        _db = new LibraryDatabase(AppPaths.DbPath);
        Banners = new BannerStore(AppPaths.BannersDir);
    }

    public BannerStore Banners { get; }

    public bool IsOpen => _db.IsOpen;

    public void Open() => _db.Open();

    public void Close() => _db.Close();

    public IReadOnlyList<Game> ListGames() => _db.ListGames();

    public Game? GetGame(long id) => _db.GetGame(id);

    public Game AddGame(GameDraft draft)
    {
        var portrait = draft.PortraitSource is null ? null : Banners.Copy(draft.PortraitSource, BannerKind.Portrait);
        var landscape = draft.LandscapeSource is null ? null : Banners.Copy(draft.LandscapeSource, BannerKind.Landscape);
        return _db.Insert(draft.Title.Trim(), draft.ReleaseYear, draft.ExePath.Trim(), portrait, landscape);
    }

    public Game? UpdateGame(long id, GameDraft draft)
    {
        var existing = _db.GetGame(id);
        if (existing is null) return null;

        var portrait = existing.PortraitFile;
        if (draft.RemovePortrait)
        {
            Banners.Delete(existing.PortraitFile);
            portrait = null;
        }
        if (draft.PortraitSource is not null)
        {
            Banners.Delete(existing.PortraitFile);
            portrait = Banners.Copy(draft.PortraitSource, BannerKind.Portrait);
        }

        var landscape = existing.LandscapeFile;
        if (draft.RemoveLandscape)
        {
            Banners.Delete(existing.LandscapeFile);
            landscape = null;
        }
        if (draft.LandscapeSource is not null)
        {
            Banners.Delete(existing.LandscapeFile);
            landscape = Banners.Copy(draft.LandscapeSource, BannerKind.Landscape);
        }

        return _db.Update(id, draft.Title.Trim(), draft.ReleaseYear, draft.ExePath.Trim(), portrait, landscape);
    }

    public bool DeleteGame(long id)
    {
        var game = _db.GetGame(id);
        if (game is null) return false;
        _db.Delete(id);
        Banners.Delete(game.PortraitFile);
        Banners.Delete(game.LandscapeFile);
        return true;
    }

    public void MarkPlayed(long id) => _db.MarkPlayed(id);

    public void AddPlayTime(long id, TimeSpan played) => _db.AddPlayTime(id, (long)played.TotalMilliseconds);

    /// <summary>Removes banner files left behind by interrupted deletes.</summary>
    public void DeleteOrphanBanners()
    {
        var games = _db.ListGames();
        Banners.DeleteOrphans(games.SelectMany(g => new[] { g.PortraitFile, g.LandscapeFile }));
    }

    public async Task ExportAsync(string zipPath)
    {
        _db.Checkpoint();
        await Task.Run(() => LibraryArchive.Export(AppPaths.DbPath, AppPaths.BannersDir, zipPath));
    }

    /// <summary>
    /// Replaces the whole library with the contents of an archive. The current library is
    /// parked in a side folder and restored if anything goes wrong.
    /// </summary>
    public async Task ImportAsync(string zipPath)
    {
        var valid = await Task.Run(() => LibraryArchive.ContainsLibrary(zipPath));
        if (!valid) throw new InvalidDataException("The archive does not contain a library.db file.");

        await ReplaceLibraryAsync(() => LibraryArchive.ExtractInto(zipPath, AppPaths.DbPath, AppPaths.BannersDir));
    }

    /// <summary>True when the earlier Electron build of this app left a library on this PC.</summary>
    public bool CanImportFromElectron => File.Exists(AppPaths.ElectronDbPath);

    public Task ImportFromElectronAsync()
    {
        return ReplaceLibraryAsync(() =>
        {
            LibraryDatabase.CopyDatabase(AppPaths.ElectronDbPath, AppPaths.DbPath);
            if (Directory.Exists(AppPaths.ElectronBannersDir))
            {
                Directory.CreateDirectory(AppPaths.BannersDir);
                foreach (var file in Directory.EnumerateFiles(AppPaths.ElectronBannersDir))
                {
                    File.Copy(file, Path.Combine(AppPaths.BannersDir, Path.GetFileName(file)), overwrite: true);
                }
            }
        });
    }

    private async Task ReplaceLibraryAsync(Action writeNewLibrary)
    {
        _db.Close();
        var parking = Path.Combine(AppPaths.DataDir, "previous-library");
        try
        {
            await Task.Run(() =>
            {
                ParkCurrentLibrary(parking);
                try
                {
                    Directory.CreateDirectory(AppPaths.BannersDir);
                    writeNewLibrary();
                }
                catch
                {
                    RestoreParkedLibrary(parking);
                    throw;
                }
                DeleteDirectoryWithRetry(parking);
            });
        }
        finally
        {
            _db.Open();
        }
    }

    private static IEnumerable<string> DatabaseFiles(string dbPath) =>
        new[] { dbPath, dbPath + "-wal", dbPath + "-shm" };

    private static void ParkCurrentLibrary(string parking)
    {
        DeleteDirectoryWithRetry(parking);
        Directory.CreateDirectory(parking);
        foreach (var file in DatabaseFiles(AppPaths.DbPath))
        {
            if (File.Exists(file)) File.Move(file, Path.Combine(parking, Path.GetFileName(file)));
        }
        if (Directory.Exists(AppPaths.BannersDir))
        {
            MoveDirectoryWithRetry(AppPaths.BannersDir, Path.Combine(parking, "banners"));
        }
    }

    private static void RestoreParkedLibrary(string parking)
    {
        foreach (var file in DatabaseFiles(AppPaths.DbPath))
        {
            if (File.Exists(file)) File.Delete(file);
        }
        DeleteDirectoryWithRetry(AppPaths.BannersDir);
        foreach (var file in DatabaseFiles(Path.Combine(parking, Path.GetFileName(AppPaths.DbPath))))
        {
            if (File.Exists(file)) File.Move(file, Path.Combine(AppPaths.DataDir, Path.GetFileName(file)));
        }
        var parkedBanners = Path.Combine(parking, "banners");
        if (Directory.Exists(parkedBanners)) MoveDirectoryWithRetry(parkedBanners, AppPaths.BannersDir);
        DeleteDirectoryWithRetry(parking);
    }

    // Banner images may still be held open by the image decoder for a moment, so the
    // folder operations retry briefly instead of failing outright.
    private static void MoveDirectoryWithRetry(string source, string destination)
    {
        Retry(() => Directory.Move(source, destination));
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        if (!Directory.Exists(path)) return;
        Retry(() => Directory.Delete(path, recursive: true));
    }

    private static void Retry(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException) when (attempt < 10) { Thread.Sleep(100); }
            catch (UnauthorizedAccessException) when (attempt < 10) { Thread.Sleep(100); }
        }
    }
}
