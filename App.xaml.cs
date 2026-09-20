using System;
using System.IO;
using Lutris.Data;
using Lutris.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Lutris;

public partial class App : Application
{
    public App()
    {
        // Developer override: LUTRIS_THEME=Light or Dark forces a theme instead of following Windows.
        if (Environment.GetEnvironmentVariable("LUTRIS_THEME") is { Length: > 0 } theme
            && Enum.TryParse<ApplicationTheme>(theme, ignoreCase: true, out var requested))
        {
            RequestedTheme = requested;
        }

        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    /// <summary>Library storage shared by every window and dialog.</summary>
    public static LibraryService Library { get; private set; } = null!;

    public static GameLauncher Launcher { get; private set; } = null!;

    public static MainWindow? CurrentWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Library = new LibraryService();
        Library.Open();
        try
        {
            Library.DeleteOrphanBanners();
        }
        catch (IOException)
        {
            // Housekeeping only; never block startup on it.
        }

        Launcher = new GameLauncher(DispatcherQueue.GetForCurrentThread());

        CurrentWindow = new MainWindow();
        CurrentWindow.Closed += (_, _) => Library.Close();
        CurrentWindow.Activate();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.AppendAllText(AppPaths.LogPath, $"{DateTimeOffset.Now:O} {e.Exception}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Nothing else to do if even logging fails.
        }
    }
}
