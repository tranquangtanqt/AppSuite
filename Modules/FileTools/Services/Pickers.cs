using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FileTools.Services;

/// <summary>Hộp thoại chọn file / thư mục / nơi lưu. App unpackaged → picker phải gắn hwnd của cửa sổ chính qua
/// InitializeWithWindow (như ImageCompare.Services.ImageFileService). Trả về null khi người dùng huỷ.</summary>
public static class Pickers
{
    public static readonly string[] TextExtensions = [".txt", ".csv", ".tsv", ".log", ".json", ".xml", ".sql", ".md", ".ini", ".dat"];

    public static async Task<string?> PickFileAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        foreach (var ext in TextExtensions)
        {
            picker.FileTypeFilter.Add(ext);
        }
        Attach(picker);
        StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public static async Task<IReadOnlyList<string>> PickFilesAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        foreach (var ext in TextExtensions)
        {
            picker.FileTypeFilter.Add(ext);
        }
        Attach(picker);
        var files = await picker.PickMultipleFilesAsync();
        return files.Select(f => f.Path).ToList();
    }

    public static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        Attach(picker);
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public static async Task<string?> PickSaveFileAsync(string suggestedName)
    {
        var ext = Path.GetExtension(suggestedName);
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName),
        };
        picker.FileTypeChoices.Add(string.IsNullOrEmpty(ext) ? "Text" : ext.TrimStart('.').ToUpperInvariant(), [string.IsNullOrEmpty(ext) ? ".txt" : ext]);
        if (!string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase))
        {
            picker.FileTypeChoices.Add("Text", [".txt"]);
        }
        Attach(picker);
        StorageFile? file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    private static void Attach(object picker)
    {
        if (App.MainWindow is { } window)
        {
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        }
    }
}
