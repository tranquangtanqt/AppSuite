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

    /// <summary>
    /// Lớp phủ toàn màn hình (Vùng chọn, Hút màu, Thước đo): cửa sổ WinUI không viền (OverlappedPresenter, SetBorderAndTitleBar
    /// false) vẫn chừa khung vài px quanh vùng client (đo ở 150%: 3 px) → ảnh nền lệch xuống-phải so với màn hình thật, vùng
    /// chọn nhìn thấy và ảnh chụp ra lệch nhau. Gọi sau Activate: nới cửa sổ để vùng client trùng đúng <paramref name="screen"/>.
    /// </summary>
    public static void AlignClientTo(Window window, RECT screen)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var origin = new POINT();
        if (!NativeMethods.ClientToScreen(hwnd, ref origin) || !NativeMethods.GetClientRect(hwnd, out var client)
            || !NativeMethods.GetWindowRect(hwnd, out var outer))
        {
            return;
        }
        int dx = screen.Left - origin.X, dy = screen.Top - origin.Y;
        int dw = screen.Width - client.Width, dh = screen.Height - client.Height;
        if (dx != 0 || dy != 0 || dw != 0 || dh != 0)
        {
            window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(outer.Left + dx, outer.Top + dy, outer.Width + dw, outer.Height + dh));
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
