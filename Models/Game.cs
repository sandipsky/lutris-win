namespace Lutris.Models;

/// <summary>
/// A game as stored in the library database. Banner properties hold file names
/// relative to the banners folder, never absolute paths.
/// </summary>
public sealed record Game(
    long Id,
    string Title,
    int? ReleaseYear,
    string ExePath,
    string? PortraitFile,
    string? LandscapeFile,
    long? LastPlayedAt,
    long TotalPlayTimeMs,
    long CreatedAt);

/// <summary>
/// Values collected by the add/edit dialog. Banner sources are absolute paths of
/// images picked by the user; they are copied into the banners folder on save.
/// </summary>
public sealed record GameDraft(
    string Title,
    int? ReleaseYear,
    string ExePath,
    string? PortraitSource,
    string? LandscapeSource,
    bool RemovePortrait = false,
    bool RemoveLandscape = false);

public enum LibraryViewMode
{
    Grid,
    List,
}

public enum LibrarySort
{
    Title,
    RecentlyPlayed,
    MostPlayed,
    RecentlyAdded,
}
