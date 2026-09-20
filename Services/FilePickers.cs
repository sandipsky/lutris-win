using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;

namespace Lutris.Services;

/// <summary>Thin wrappers over the Windows App SDK file pickers.</summary>
public static class FilePickers
{
    public static async Task<string?> PickExecutableAsync(WindowId owner)
    {
        var picker = new FileOpenPicker(owner)
        {
            CommitButtonText = "Select",
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
            ViewMode = PickerViewMode.List,
        };
        foreach (var ext in new[] { ".exe", ".bat", ".cmd", ".lnk" })
        {
            picker.FileTypeFilter.Add(ext);
        }
        var result = await picker.PickSingleFileAsync();
        return result?.Path;
    }

    public static async Task<string?> PickImageAsync(WindowId owner)
    {
        var picker = new FileOpenPicker(owner)
        {
            CommitButtonText = "Use image",
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail,
        };
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" })
        {
            picker.FileTypeFilter.Add(ext);
        }
        var result = await picker.PickSingleFileAsync();
        return result?.Path;
    }

    public static async Task<string?> PickArchiveToOpenAsync(WindowId owner)
    {
        var picker = new FileOpenPicker(owner)
        {
            CommitButtonText = "Import",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".zip");
        var result = await picker.PickSingleFileAsync();
        return result?.Path;
    }

    public static async Task<string?> PickArchiveToSaveAsync(WindowId owner)
    {
        var picker = new FileSavePicker(owner)
        {
            CommitButtonText = "Export",
            SuggestedFileName = "lutris-library",
            DefaultFileExtension = ".zip",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeChoices.Add("Library archive", new List<string> { ".zip" });
        var result = await picker.PickSaveFileAsync();
        return result?.Path;
    }
}
