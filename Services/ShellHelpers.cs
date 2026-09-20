using System.Diagnostics;
using System.IO;

namespace Lutris.Services;

public static class ShellHelpers
{
    /// <summary>Opens File Explorer with the given file selected.</summary>
    public static bool RevealInExplorer(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"")
        {
            UseShellExecute = true,
        });
        return true;
    }

    public static void OpenFolder(string directory)
    {
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }
}
