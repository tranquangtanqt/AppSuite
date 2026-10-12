using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MeRon.Services;

/// <summary>Hộp thoại chọn thư mục. App unpackaged → picker phải gắn hwnd của cửa sổ chính. Trả về null khi người dùng huỷ.</summary>
public static class Pickers
{
    public static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        if (App.MainWindow is { } window)
        {
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        }
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
