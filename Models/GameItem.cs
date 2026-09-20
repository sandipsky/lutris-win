using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Lutris.Data;
using Lutris.Services;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Lutris.Models;

/// <summary>
/// Presentation wrapper around a <see cref="Game"/>. Instances are kept alive across
/// reloads so decoded banner images are reused and the grid does not flicker.
/// </summary>
public sealed partial class GameItem : ObservableObject
{
    private readonly BannerStore _banners;

    public GameItem(Game game, BannerStore banners, bool isRunning)
    {
        _banners = banners;
        Model = game;
        Title = YearText = ExePath = SubtitleText = PlayTimeText = LastPlayedText = AddedText = string.Empty;
        IsRunning = isRunning;
        ApplyText(game);
        RebuildImages(game);
    }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string YearText { get; set; }

    [ObservableProperty]
    public partial bool HasYear { get; set; }

    [ObservableProperty]
    public partial string ExePath { get; set; }

    [ObservableProperty]
    public partial string SubtitleText { get; set; }

    [ObservableProperty]
    public partial string PlayTimeText { get; set; }

    [ObservableProperty]
    public partial string LastPlayedText { get; set; }

    [ObservableProperty]
    public partial string AddedText { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial ImageSource? PortraitThumb { get; set; }

    [ObservableProperty]
    public partial bool HasPortrait { get; set; }

    [ObservableProperty]
    public partial ImageSource? RowThumb { get; set; }

    [ObservableProperty]
    public partial bool HasRowThumb { get; set; }

    [ObservableProperty]
    public partial ImageSource? HeroImage { get; set; }

    [ObservableProperty]
    public partial bool HasHero { get; set; }

    public Game Model { get; private set; }

    public long Id => Model.Id;

    public string? PortraitPath => _banners.AbsolutePath(Model.PortraitFile);

    public string? LandscapePath => _banners.AbsolutePath(Model.LandscapeFile);

    public void Update(Game game)
    {
        var previous = Model;
        Model = game;
        ApplyText(game);
        if (previous.PortraitFile != game.PortraitFile || previous.LandscapeFile != game.LandscapeFile)
        {
            RebuildImages(game);
        }
    }

    private void ApplyText(Game game)
    {
        Title = game.Title;
        HasYear = game.ReleaseYear.HasValue;
        YearText = game.ReleaseYear?.ToString() ?? string.Empty;
        ExePath = game.ExePath;
        PlayTimeText = Formatting.Duration(game.TotalPlayTimeMs);
        LastPlayedText = Formatting.RelativeTime(game.LastPlayedAt);
        AddedText = Formatting.AbsoluteDate(game.CreatedAt);

        // "Last played yesterday" / "Last played 3 days ago", but "Last played on May 10".
        var played = game.LastPlayedAt is null
            ? "Never played"
            : char.IsDigit(LastPlayedText[^1])
                ? "Last played on " + LastPlayedText
                : "Last played " + LowerFirst(LastPlayedText);
        SubtitleText = HasYear ? $"{YearText}  ·  {played}" : played;
    }

    private void RebuildImages(Game game)
    {
        var portrait = _banners.AbsolutePath(game.PortraitFile);
        var landscape = _banners.AbsolutePath(game.LandscapeFile);

        PortraitThumb = Load(portrait, 400);
        HasPortrait = PortraitThumb is not null;

        RowThumb = Load(landscape ?? portrait, 320);
        HasRowThumb = RowThumb is not null;

        HeroImage = Load(landscape ?? portrait, 900);
        HasHero = HeroImage is not null;
    }

    private static ImageSource? Load(string? path, int decodeWidth)
    {
        if (path is null) return null;
        return new BitmapImage(new Uri(path))
        {
            DecodePixelWidth = decodeWidth,
            DecodePixelType = DecodePixelType.Logical,
        };
    }

    private static string LowerFirst(string text) =>
        text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];
}
