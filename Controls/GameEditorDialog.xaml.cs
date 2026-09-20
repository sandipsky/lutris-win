using System;
using System.IO;
using System.Text;
using Lutris.Data;
using Lutris.Models;
using Lutris.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;
using Windows.Globalization.NumberFormatting;

namespace Lutris.Controls;

/// <summary>
/// Add/edit form for a game. After <c>ShowAsync</c> returns Primary, <see cref="Result"/>
/// holds the validated values.
/// </summary>
public sealed partial class GameEditorDialog : ContentDialog
{
    private readonly WindowId _owner;
    private string? _portraitSource;
    private string? _landscapeSource;
    private bool _removePortrait;
    private bool _removeLandscape;

    public GameEditorDialog(XamlRoot xamlRoot, WindowId owner, BannerStore banners, Game? existing)
    {
        InitializeComponent();
        XamlRoot = xamlRoot;
        _owner = owner;

        // Years must not be shown as "2,017".
        YearBox.NumberFormatter = new DecimalFormatter
        {
            IntegerDigits = 1,
            FractionDigits = 0,
            IsGrouped = false,
        };

        if (existing is not null)
        {
            Title = "Edit game";
            PrimaryButtonText = "Save";
            TitleBox.Text = existing.Title;
            ExeBox.Text = existing.ExePath;
            if (existing.ReleaseYear is int year) YearBox.Value = year;
            ShowPreview(BannerKind.Portrait, banners.AbsolutePath(existing.PortraitFile));
            ShowPreview(BannerKind.Landscape, banners.AbsolutePath(existing.LandscapeFile));
        }
    }

    public GameDraft? Result { get; private set; }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var title = TitleBox.Text.Trim();
        var exe = ExeBox.Text.Trim();

        if (title.Length == 0)
        {
            Reject(args, "Give the game a title.", TitleBox);
            return;
        }
        if (exe.Length == 0)
        {
            Reject(args, "Choose the executable that starts the game.", ExeBox);
            return;
        }

        int? year = null;
        if (!double.IsNaN(YearBox.Value))
        {
            var value = (int)Math.Round(YearBox.Value);
            if (value < 1950 || value > 2100)
            {
                Reject(args, "The release year must be between 1950 and 2100.", YearBox);
                return;
            }
            year = value;
        }

        ValidationBar.IsOpen = false;
        Result = new GameDraft(title, year, exe, _portraitSource, _landscapeSource, _removePortrait, _removeLandscape);
    }

    private void Reject(ContentDialogButtonClickEventArgs args, string message, Control focusTarget)
    {
        args.Cancel = true;
        ValidationBar.Message = message;
        ValidationBar.IsOpen = true;
        focusTarget.Focus(FocusState.Programmatic);
    }

    private async void OnBrowseExe(object sender, RoutedEventArgs e)
    {
        var path = await FilePickers.PickExecutableAsync(_owner);
        if (path is null) return;
        ExeBox.Text = path;
        if (TitleBox.Text.Trim().Length == 0)
        {
            TitleBox.Text = SuggestTitle(path);
        }
    }

    private async void OnChoosePortrait(object sender, RoutedEventArgs e)
    {
        var path = await FilePickers.PickImageAsync(_owner);
        if (path is null) return;
        _portraitSource = path;
        _removePortrait = false;
        ShowPreview(BannerKind.Portrait, path);
    }

    private async void OnChooseLandscape(object sender, RoutedEventArgs e)
    {
        var path = await FilePickers.PickImageAsync(_owner);
        if (path is null) return;
        _landscapeSource = path;
        _removeLandscape = false;
        ShowPreview(BannerKind.Landscape, path);
    }

    private void OnClearPortrait(object sender, RoutedEventArgs e)
    {
        _portraitSource = null;
        _removePortrait = true;
        HidePreview(BannerKind.Portrait);
    }

    private void OnClearLandscape(object sender, RoutedEventArgs e)
    {
        _landscapeSource = null;
        _removeLandscape = true;
        HidePreview(BannerKind.Landscape);
    }

    private void ShowPreview(BannerKind kind, string? path)
    {
        if (path is null) return;
        var (preview, brush, chooseButton, clearButton) = PreviewControls(kind);
        brush.ImageSource = new BitmapImage(new Uri(path))
        {
            DecodePixelWidth = 480,
            DecodePixelType = DecodePixelType.Logical,
        };
        preview.Visibility = Visibility.Visible;
        clearButton.Visibility = Visibility.Visible;
        chooseButton.Content = "Replace image";
    }

    private void HidePreview(BannerKind kind)
    {
        var (preview, brush, chooseButton, clearButton) = PreviewControls(kind);
        brush.ImageSource = null;
        preview.Visibility = Visibility.Collapsed;
        clearButton.Visibility = Visibility.Collapsed;
        chooseButton.Content = "Choose image";
    }

    private (Rectangle Preview, ImageBrush Brush, Button Choose, Button Clear) PreviewControls(BannerKind kind) =>
        kind == BannerKind.Portrait
            ? (PortraitPreview, PortraitBrush, PortraitChooseButton, PortraitClearButton)
            : (LandscapePreview, LandscapeBrush, LandscapeChooseButton, LandscapeClearButton);

    /// <summary>Turns "hollow_knight" or "HollowKnight" into "Hollow Knight".</summary>
    private static string SuggestTitle(string exePath)
    {
        var raw = Path.GetFileNameWithoutExtension(exePath).Replace('_', ' ').Replace('-', ' ');
        var spaced = new StringBuilder();
        for (var i = 0; i < raw.Length; i++)
        {
            if (i > 0 && char.IsUpper(raw[i]) && char.IsLower(raw[i - 1])) spaced.Append(' ');
            spaced.Append(raw[i]);
        }
        var words = spaced.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            if (char.IsLower(words[i][0])) words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..];
        }
        return string.Join(' ', words);
    }
}
