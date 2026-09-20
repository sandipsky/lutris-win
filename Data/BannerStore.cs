using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace Lutris.Data;

public enum BannerKind
{
    Portrait,
    Landscape,
}

/// <summary>
/// Owns the folder of banner images. Games reference banners by file name only.
/// </summary>
public sealed class BannerStore
{
    private readonly string _dir;

    public BannerStore(string dir)
    {
        _dir = dir;
    }

    public string Directory => _dir;

    /// <summary>Copies an image the user picked into the store and returns the new file name.</summary>
    public string Copy(string sourcePath, BannerKind kind)
    {
        System.IO.Directory.CreateDirectory(_dir);
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) ext = ".png";
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var fileName = $"{kind.ToString().ToLowerInvariant()}-{id}{ext}";
        File.Copy(sourcePath, Path.Combine(_dir, fileName), overwrite: false);
        return fileName;
    }

    public string? AbsolutePath(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return null;
        var path = Path.Combine(_dir, fileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Deletes a banner. The image decoder may still hold the file for a moment after the
    /// UI stops showing it, so a few quick retries are made; leftovers are swept on startup.
    /// </summary>
    public void Delete(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return;
        var path = Path.Combine(_dir, fileName);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            catch (IOException) { Thread.Sleep(50); }
            catch (UnauthorizedAccessException) { Thread.Sleep(50); }
        }
    }

    /// <summary>Removes banner files that no game references any more.</summary>
    public void DeleteOrphans(IEnumerable<string?> referencedFileNames)
    {
        if (!System.IO.Directory.Exists(_dir)) return;
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in referencedFileNames)
        {
            if (!string.IsNullOrEmpty(name)) referenced.Add(name);
        }
        foreach (var file in System.IO.Directory.EnumerateFiles(_dir))
        {
            if (referenced.Contains(Path.GetFileName(file))) continue;
            try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public void DeleteAll()
    {
        if (System.IO.Directory.Exists(_dir))
        {
            System.IO.Directory.Delete(_dir, recursive: true);
        }
        System.IO.Directory.CreateDirectory(_dir);
    }
}
