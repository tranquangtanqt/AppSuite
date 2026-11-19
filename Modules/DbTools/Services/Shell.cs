using System.Diagnostics;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DbTools.Services;

/// <summary>Mở file / thư mục bằng Windows và hộp thoại chọn thư mục (app unpackaged → picker gắn hwnd cửa sổ chính).</summary>
public static class Shell
{
    public static void Open(string path)
    {
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        else if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    /// <summary>Mở thư mục chứa và chọn sẵn file.</summary>
    public static void Reveal(string path)
    {
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        else
        {
            Open(path);
        }
    }

    /// <summary>Chọn 1 file; <paramref name="extensions"/> dạng ".dump" (hoặc "*" = mọi file).</summary>
    public static async Task<string?> PickFileAsync(params string[] extensions)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        foreach (var ext in extensions)
        {
            picker.FileTypeFilter.Add(ext);
        }
        if (App.MainWindow is { } window)
        {
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        }
        StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        if (App.MainWindow is { } window)
        {
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        }
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
