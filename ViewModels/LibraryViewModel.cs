using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Lutris.Models;
using Lutris.Services;

namespace Lutris.ViewModels;

/// <summary>
/// Bindable state for the library window: the filtered, sorted list of games plus
/// search, sort, view mode and selection.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly LibraryService _library;
    private readonly GameLauncher _launcher;
    private readonly Dictionary<long, GameItem> _items = new();
    private List<Game> _all = new();
    private IReadOnlyList<GpuAdapter> _gpus = Array.Empty<GpuAdapter>();

    public LibraryViewModel(LibraryService library, GameLauncher launcher)
    {
        _library = library;
        _launcher = launcher;
        SearchText = string.Empty;
        GpuOptions = Array.Empty<string>();
        _launcher.GameStarted += id => SetRunning(id, true);
        _launcher.GameExited += (id, _) => SetRunning(id, false);
    }

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGridView), nameof(IsListView))]
    public partial LibraryViewMode ViewMode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortLabel))]
    public partial LibrarySort Sort { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial GameItem? SelectedItem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryEmpty), nameof(HasNoMatches), nameof(CountText))]
    public partial int TotalCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoMatches))]
    public partial int VisibleCount { get; set; }

    /// <summary>"Default" followed by each graphics card, filled in by <see cref="LoadGpusAsync"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGpuChoice))]
    public partial IReadOnlyList<string> GpuOptions { get; set; }

    public ObservableCollection<GameItem> Games { get; } = new();

    public bool HasSelection => SelectedItem is not null;

    public bool IsLibraryEmpty => TotalCount == 0;

    public bool HasNoMatches => TotalCount > 0 && VisibleCount == 0;

    public bool IsGridView => ViewMode == LibraryViewMode.Grid;

    public bool IsListView => ViewMode == LibraryViewMode.List;

    public string CountText => TotalCount == 1 ? "1 game" : $"{TotalCount} games";

    public string SortLabel => Sort switch
    {
        LibrarySort.RecentlyPlayed => "Recently played",
        LibrarySort.MostPlayed => "Most played",
        LibrarySort.RecentlyAdded => "Recently added",
        _ => "Title",
    };

    /// <summary>True when the PC has more than one graphics card, so the choice is worth showing.</summary>
    public bool HasGpuChoice => _gpus.Count > 1;

    /// <summary>Finds the graphics cards once at startup; the selector stays hidden until this completes.</summary>
    public async Task LoadGpusAsync()
    {
        try
        {
            _gpus = await GpuPreferences.ListAdaptersAsync();
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or UnauthorizedAccessException or InvalidOperationException)
        {
            _gpus = Array.Empty<GpuAdapter>();
        }
        GpuOptions = new[] { "Default" }.Concat(_gpus.Select(g => g.Name)).ToList();
    }

    /// <summary>
    /// Index into <see cref="GpuOptions"/> of the card Windows has the game pinned to; 0 (Default)
    /// when Windows decides or the card is no longer present.
    /// </summary>
    public int GpuOptionIndex(GameItem item)
    {
        string? id;
        try
        {
            id = GpuPreferences.GetSpecificGpu(item.ExePath);
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            return 0;
        }
        if (id is null) return 0;
        for (var i = 0; i < _gpus.Count; i++)
        {
            if (string.Equals(_gpus[i].Id, id, StringComparison.OrdinalIgnoreCase)) return i + 1;
        }
        return 0;
    }

    /// <summary>
    /// Pins the game to the card at <paramref name="optionIndex"/> in <see cref="GpuOptions"/>
    /// (0 = Default). Returns an error message when Windows refused the change.
    /// </summary>
    public string? SetGpuOption(GameItem item, int optionIndex)
    {
        var adapter = optionIndex > 0 && optionIndex <= _gpus.Count ? _gpus[optionIndex - 1] : null;
        try
        {
            GpuPreferences.SetSpecificGpu(item.ExePath, adapter?.Id);
            return null;
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    /// <summary>Re-reads the library from disk and refreshes the visible list in place.</summary>
    public void Reload()
    {
        _all = _library.ListGames().ToList();
        TotalCount = _all.Count;

        var seen = new HashSet<long>();
        foreach (var game in _all)
        {
            if (_items.TryGetValue(game.Id, out var item))
            {
                item.Update(game);
            }
            else
            {
                _items[game.Id] = new GameItem(game, _library.Banners, _launcher.IsRunning(game.Id));
            }
            seen.Add(game.Id);
        }
        foreach (var id in _items.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _items.Remove(id);
        }

        ApplyFilter();

        if (SelectedItem is not null && !_items.ContainsKey(SelectedItem.Id))
        {
            SelectedItem = null;
        }
    }

    public void SelectById(long id) => SelectedItem = _items.GetValueOrDefault(id);

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSortChanged(LibrarySort value) => ApplyFilter();

    private void ApplyFilter()
    {
        IEnumerable<Game> query = _all;

        var search = SearchText.Trim();
        if (search.Length > 0)
        {
            query = query.Where(g => g.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }

        var byTitle = Comparer<Game>.Create((a, b) =>
            StringComparer.CurrentCultureIgnoreCase.Compare(a.Title, b.Title));

        query = Sort switch
        {
            LibrarySort.RecentlyPlayed => query.OrderByDescending(g => g.LastPlayedAt ?? long.MinValue).ThenBy(g => g, byTitle),
            LibrarySort.MostPlayed => query.OrderByDescending(g => g.TotalPlayTimeMs).ThenBy(g => g, byTitle),
            LibrarySort.RecentlyAdded => query.OrderByDescending(g => g.CreatedAt).ThenBy(g => g, byTitle),
            _ => query.OrderBy(g => g, byTitle),
        };

        SyncCollection(query.Select(g => _items[g.Id]).ToList());
        VisibleCount = Games.Count;
    }

    // Applies the desired order with minimal add/remove operations so the ItemsView
    // keeps its scroll position and existing item containers.
    private void SyncCollection(List<GameItem> desired)
    {
        var wanted = new HashSet<GameItem>(desired);
        for (var i = Games.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(Games[i])) Games.RemoveAt(i);
        }
        for (var i = 0; i < desired.Count; i++)
        {
            var item = desired[i];
            var current = Games.IndexOf(item);
            if (current == i) continue;
            if (current >= 0) Games.RemoveAt(current);
            Games.Insert(i, item);
        }
    }

    private void SetRunning(long id, bool running)
    {
        if (_items.TryGetValue(id, out var item)) item.IsRunning = running;
    }
}
