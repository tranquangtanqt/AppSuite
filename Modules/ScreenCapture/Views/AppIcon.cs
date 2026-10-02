using Microsoft.UI.Xaml;

namespace ScreenCapture.Views;

/// <summary>Icon camera của app (Assets\ScreenCapture.ico, chép cạnh exe) cho thanh tiêu đề + nút taskbar / Alt+Tab. App
/// unpackaged không tự lấy icon exe cho cửa sổ WinUI - không gán thì taskbar hiện icon trống (khay vẫn có vì
/// TrayIconService tự vẽ).</summary>
internal static class AppIcon
{
    private static readonly string IconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ScreenCapture.ico");

    public static void Apply(Window window)
    {
        if (File.Exists(IconPath))
        {
            window.AppWindow.SetIcon(IconPath);
        }
    }
}
