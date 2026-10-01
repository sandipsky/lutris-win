using System;
using System.ComponentModel;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Lutris.Controls;
using Lutris.Data;
using Lutris.Interop;
using Lutris.Models;
using Lutris.Services;
using Lutris.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace Lutris;

public sealed partial class MainWindow : Window
{
    private const int DefaultWidth = 1280;
    private const int DefaultHeight = 840;
    private const int MinWidth = 900;
    private const int MinHeight = 600;
    private const double WideToolbarWindowWidth = 1120;

    private readonly DispatcherQueueTimer _toastTimer;
    private bool _syncingSelection;
    private double _libraryOffset;
    private bool _libraryReflowing;

    public MainWindow()
    {
        ViewModel = new LibraryViewModel(App.Library, App.Launcher);
        InitializeComponent();
        ConfigureWindow();
        ConfigureDetailsPaneAnimations();

        _toastTimer = DispatcherQueue.CreateTimer();
        _toastTimer.Interval = TimeSpan.FromSeconds(4);
        _toastTimer.IsRepeating = false;
        _toastTimer.Tick += (_, _) => Toast.IsOpen = false;

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        App.Launcher.GameExited += Launcher_GameExited;
        Root.Loaded += (_, _) => UpdateTitleBarInsets();
        Root.SizeChanged += Root_SizeChanged;
        AppTitleBar.SizeChanged += (_, _) => UpdateTitleBarInsets();
        LibraryView.Loaded += (_, _) => TrackLibraryScroll();
        LibraryView.SizeChanged += (_, _) => KeepLibraryAtTop();

        ViewModel.Reload();
        var canImport = App.Library.CanImportFromElectron;
        ImportElectronItem.Visibility = canImport ? Visibility.Visible : Visibility.Collapsed;
        MigrationBar.IsOpen = canImport && ViewModel.IsLibraryEmpty;
    }

    public LibraryViewModel ViewModel { get; }

    // Helpers used by x:Bind function bindings in the XAML.
    public static bool Not(bool value) => !value;

    public static Visibility VisibleIfNot(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static string PlayLabel(bool isRunning) => isRunning ? "Running" : "Play";

    private void ConfigureWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var appWindow = AppWindow;
        appWindow.Title = "Lutris";
        appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        try
        {
            appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Lutris.ico"));
        }
        catch (Exception)
        {
            // A missing icon file is cosmetic; keep starting.
        }

        // Size for the display the window will actually open on; the window's own DPI is not
        // reliable before it has been shown.
        var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Nearest);
        var scale = WindowInterop.GetDpiScale(displayArea);
        var workArea = displayArea.WorkArea;
        var width = Math.Min((int)(DefaultWidth * scale), (int)(workArea.Width * 0.94));
        var height = Math.Min((int)(DefaultHeight * scale), (int)(workArea.Height * 0.94));
        appWindow.MoveAndResize(new RectInt32(
            workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2,
            width,
            height));

        WindowInterop.SetMinimumSize(this, MinWidth, MinHeight);
    }

    /// <summary>
    /// Keeps the caption-button insets in sync with layout so the app title never sits under the
    /// window controls. The whole strip is a drag region.
    /// </summary>
    private void UpdateTitleBarInsets()
    {
        if (AppTitleBar.XamlRoot is null) return;
        var scale = AppTitleBar.XamlRoot.RasterizationScale;

        LeftPaddingColumn.Width = new GridLength(AppWindow.TitleBar.LeftInset / scale);
        RightPaddingColumn.Width = new GridLength(AppWindow.TitleBar.RightInset / scale);
    }

    // The toolbar keeps the heading, search box, sort, view toggles and actions on one line, so at
    // narrow widths the search box gives up some room rather than overlapping the heading.
    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SearchBox.Width = e.NewSize.Width >= WideToolbarWindowWidth ? 300 : 220;
    }

    /// <summary>
    /// Slides the details pane in from the right edge when it is shown and back out when it is
    /// hidden. Implicit show/hide animations run on the compositor whenever Visibility changes, and
    /// the hide animation finishes before the pane actually collapses.
    /// </summary>
    private void ConfigureDetailsPaneAnimations()
    {
        ElementCompositionPreview.SetIsTranslationEnabled(DetailsPane, true);
        var compositor = ElementCompositionPreview.GetElementVisual(DetailsPane).Compositor;
        var offscreen = new Vector3((float)DetailsPane.Width, 0f, 0f);

        // Fluent motion curves: decelerate on entry, accelerate on exit.
        var decelerate = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));
        var accelerate = compositor.CreateCubicBezierEasingFunction(new Vector2(0.9f, 0.1f), new Vector2(1f, 0.2f));

        var slideIn = compositor.CreateVector3KeyFrameAnimation();
        slideIn.Target = "Translation";
        slideIn.InsertKeyFrame(0f, offscreen);
        slideIn.InsertKeyFrame(1f, Vector3.Zero, decelerate);
        slideIn.Duration = TimeSpan.FromMilliseconds(300);

        var fadeIn = compositor.CreateScalarKeyFrameAnimation();
        fadeIn.Target = "Opacity";
        fadeIn.InsertKeyFrame(0f, 0f);
        fadeIn.InsertKeyFrame(1f, 1f, decelerate);
        fadeIn.Duration = TimeSpan.FromMilliseconds(200);

        var show = compositor.CreateAnimationGroup();
        show.Add(slideIn);
        show.Add(fadeIn);
        ElementCompositionPreview.SetImplicitShowAnimation(DetailsPane, show);

        var slideOut = compositor.CreateVector3KeyFrameAnimation();
        slideOut.Target = "Translation";
        slideOut.InsertKeyFrame(1f, offscreen, accelerate);
        slideOut.Duration = TimeSpan.FromMilliseconds(200);

        var fadeOut = compositor.CreateScalarKeyFrameAnimation();
        fadeOut.Target = "Opacity";
        fadeOut.InsertKeyFrame(1f, 0f, accelerate);
        fadeOut.Duration = TimeSpan.FromMilliseconds(200);

        var hide = compositor.CreateAnimationGroup();
        hide.Add(slideOut);
        hide.Add(fadeOut);
        ElementCompositionPreview.SetImplicitHideAnimation(DetailsPane, hide);
    }

    // When the window is resized the grid reflows and the ItemsView anchors on the selected card,
    // which can nudge the first row under the top edge. If the user was at the top, keep them there.
    private void TrackLibraryScroll()
    {
        if (LibraryView.ScrollView is not { } scrollView) return;
        scrollView.ViewChanged += (sender, _) =>
        {
            if (!_libraryReflowing) _libraryOffset = sender.VerticalOffset;
        };
    }

    private void KeepLibraryAtTop()
    {
        if (_libraryOffset >= 1 || LibraryView.ScrollView is not { } scrollView) return;
        _libraryReflowing = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (scrollView.VerticalOffset > 0)
            {
                scrollView.ScrollTo(0, 0, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
            }
            _libraryReflowing = false;
        });
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.SelectedItem))
        {
            SyncSelectionToView();
            UpdateDetailsPane();
        }
    }

    private void SyncSelectionToView()
    {
        if (_syncingSelection) return;
        _syncingSelection = true;
        try
        {
            var item = ViewModel.SelectedItem;
            if (item is null)
            {
                LibraryView.DeselectAll();
            }
            else if (!ReferenceEquals(LibraryView.SelectedItem, item))
            {
                var index = ViewModel.Games.IndexOf(item);
                if (index >= 0) LibraryView.Select(index);
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void LibraryView_SelectionChanged(ItemsView sender, ItemsViewSelectionChangedEventArgs args)
    {
        if (_syncingSelection) return;
        _syncingSelection = true;
        try
        {
            ViewModel.SelectedItem = sender.SelectedItem as GameItem;
        }
        finally
        {
            _syncingSelection = false;
        }
        UpdateDetailsPane();
    }

    private void LibraryView_ItemInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is GameItem item) ViewModel.SelectedItem = item;
    }

    // Showing or hiding the pane plays the animations from ConfigureDetailsPaneAnimations; changing
    // the selection while it is already open only swaps the bound content.
    private void UpdateDetailsPane()
    {
        DetailsPane.Visibility = ViewModel.HasSelection ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseDetails_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedItem = null;

    // ----- Search, sort and view mode -----

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) ViewModel.SearchText = sender.Text;
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        ViewModel.SearchText = args.QueryText;
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        ViewModel.SearchText = string.Empty;
    }

    private void SortItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioMenuFlyoutItem { Tag: string tag } && Enum.TryParse<LibrarySort>(tag, out var sort))
        {
            ViewModel.Sort = sort;
        }
    }

    private void GridViewToggle_Click(object sender, RoutedEventArgs e) => SetViewMode(LibraryViewMode.Grid);

    private void ListViewToggle_Click(object sender, RoutedEventArgs e) => SetViewMode(LibraryViewMode.List);

    private void SetViewMode(LibraryViewMode mode)
    {
        ViewModel.ViewMode = mode;
        GridViewToggle.IsChecked = mode == LibraryViewMode.Grid;
        ListViewToggle.IsChecked = mode == LibraryViewMode.List;

        var isGrid = mode == LibraryViewMode.Grid;
        LibraryView.Layout = (Layout)Root.Resources[isGrid ? "GridLayout" : "ListLayout"];
        LibraryView.ItemTemplate = (DataTemplate)Root.Resources[isGrid ? "GameCardTemplate" : "GameRowTemplate"];
        SyncSelectionToView();
    }

    // ----- Game actions -----

    private static GameItem? ItemFrom(object sender) => (sender as FrameworkElement)?.Tag as GameItem;

    private void PlayMenu_Click(object sender, RoutedEventArgs e)
    {
        if (ItemFrom(sender) is { } item) Play(item);
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (ItemFrom(sender) is { } item) Play(item);
    }

    private void PlaySelected_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedItem is { } item) Play(item);
    }

    private void GameItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ItemFrom(sender) is { } item)
        {
            e.Handled = true;
            Play(item);
        }
    }

    private void Play(GameItem item)
    {
        var result = App.Launcher.Launch(item.Model);
        if (!result.Ok)
        {
            ShowToast(result.Error ?? "The game could not be started.", InfoBarSeverity.Error);
            return;
        }
        App.Library.MarkPlayed(item.Id);
        ViewModel.Reload();
        ShowToast($"Launching {item.Title}…");
    }

    private void Launcher_GameExited(long gameId, TimeSpan played)
    {
        // A game can outlive the window; by then the library is already closed.
        if (!App.Library.IsOpen) return;
        App.Library.AddPlayTime(gameId, played);
        ViewModel.Reload();
    }

    private async void EditMenu_Click(object sender, RoutedEventArgs e)
    {
        if (ItemFrom(sender) is { } item) await ShowEditorAsync(item.Model);
    }

    private async void EditSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedItem is { } item) await ShowEditorAsync(item.Model);
    }

    private async void AddGame_Click(object sender, RoutedEventArgs e) => await ShowEditorAsync(null);

    private async Task ShowEditorAsync(Game? existing)
    {
        var dialog = new GameEditorDialog(Content.XamlRoot, AppWindow.Id, App.Library.Banners, existing);
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || dialog.Result is null) return;

        try
        {
            if (existing is null)
            {
                var game = App.Library.AddGame(dialog.Result);
                ViewModel.Reload();
                ViewModel.SelectById(game.Id);
                ShowToast($"Added {game.Title}", InfoBarSeverity.Success);
            }
            else
            {
                App.Library.UpdateGame(existing.Id, dialog.Result);
                ViewModel.Reload();
                ShowToast("Changes saved", InfoBarSeverity.Success);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            ShowToast(ex.Message, InfoBarSeverity.Error);
        }
    }

    private void RevealMenu_Click(object sender, RoutedEventArgs e)
    {
        if (ItemFrom(sender) is { } item) Reveal(item);
    }

    private void RevealSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedItem is { } item) Reveal(item);
    }

    private void Reveal(GameItem item)
    {
        if (!ShellHelpers.RevealInExplorer(item.ExePath))
        {
            ShowToast($"The executable was not found: {item.ExePath}", InfoBarSeverity.Error);
        }
    }

    private async void RemoveMenu_Click(object sender, RoutedEventArgs e)
    {
        if (ItemFrom(sender) is { } item) await RemoveAsync(item);
    }

    private async void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedItem is { } item) await RemoveAsync(item);
    }

    private async Task RemoveAsync(GameItem item)
    {
        var confirmed = await ConfirmAsync(
            $"Remove {item.Title}?",
            "This removes the entry and its banners from your library. The game's own files are not touched.",
            "Remove");
        if (!confirmed) return;

        if (ReferenceEquals(ViewModel.SelectedItem, item)) ViewModel.SelectedItem = null;
        App.Library.DeleteGame(item.Id);
        ViewModel.Reload();
        ShowToast($"Removed {item.Title}");
    }

    // ----- Library import / export -----

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var path = await FilePickers.PickArchiveToSaveAsync(AppWindow.Id);
        if (path is null) return;
        try
        {
            await App.Library.ExportAsync(path);
            ShowToast($"Library exported to {Path.GetFileName(path)}", InfoBarSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            ShowToast($"Export failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var path = await FilePickers.PickArchiveToOpenAsync(AppWindow.Id);
        if (path is null) return;

        var confirmed = await ConfirmAsync(
            "Replace your library?",
            "Importing replaces every game and banner currently in your library. This cannot be undone.",
            "Replace library");
        if (!confirmed) return;

        await ReplaceLibraryAsync(() => App.Library.ImportAsync(path), "Library imported");
    }

    private async void ImportElectron_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsLibraryEmpty)
        {
            var confirmed = await ConfirmAsync(
                "Replace your library?",
                "Importing replaces every game and banner currently in your library with the ones from the previous Lutris app.",
                "Replace library");
            if (!confirmed) return;
        }

        await ReplaceLibraryAsync(App.Library.ImportFromElectronAsync, "Your library was brought over from the previous Lutris app");
        MigrationBar.IsOpen = false;
    }

    private async Task ReplaceLibraryAsync(Func<Task> import, string successMessage)
    {
        ViewModel.SelectedItem = null;
        try
        {
            await import();
            ViewModel.Reload();
            ShowToast(successMessage, InfoBarSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {
            ViewModel.Reload();
            ShowToast($"Import failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) => ShellHelpers.OpenFolder(AppPaths.DataDir);

    // ----- Keyboard shortcuts -----

    private void AddAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = ShowEditorAsync(null);
    }

    private void SearchAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SearchBox.Focus(FocusState.Programmatic);
    }

    private void RefreshAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ViewModel.Reload();
    }

    // ----- Dialogs and notifications -----

    private async Task<bool> ConfirmAsync(string title, string message, string primaryText)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = primaryText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void ShowToast(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        _toastTimer.Stop();
        Toast.Severity = severity;
        Toast.Message = message;
        Toast.IsOpen = true;
        _toastTimer.Start();
    }
}
