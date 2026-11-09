using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using ScreenCapture.Services.Interop;
using WinRT.Interop;

namespace ScreenCapture.Views;

/// <summary>Thanh tiêu đề theo giao diện sáng / tối của Windows (mặc định luôn sáng) + cỡ tối thiểu của cửa sổ.</summary>
internal static class WindowChrome
{
    /// <param name="minWidth">Cỡ tối thiểu theo đơn vị logical (nhân DPI vì AppWindow tính pixel vật lý).</param>
    public static void Apply(Window window, double minWidth, double minHeight)
    {
        window.AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            var scale = NativeMethods.GetDpiForWindow(WindowNative.GetWindowHandle(window)) / 96.0;
            presenter.PreferredMinimumWidth = (int)(minWidth * scale);
            presenter.PreferredMinimumHeight = (int)(minHeight * scale);
        }
    }

    /// <summary>Đặt cỡ cửa sổ (logical) nhưng không vượt vùng làm việc của màn hình - màn thấp (vd 1366×768 ở 150%) thì hàng
    /// nút cuối cửa sổ không bị rơi xuống dưới mép màn hình.</summary>
    public static void Resize(Window window, double width, double height)
    {
        var scale = NativeMethods.GetDpiForWindow(WindowNative.GetWindowHandle(window)) / 96.0;
        var work = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int w = Math.Min((int)(width * scale), work.Width), h = Math.Min((int)(height * scale), work.Height);
        window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h));
    }
}
