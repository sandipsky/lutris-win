using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

    public LibraryViewModel(LibraryService library, GameLauncher launcher)
    {
        _library = library;
        _launcher = launcher;
        SearchText = string.Empty;
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
