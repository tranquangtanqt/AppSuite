using System.Diagnostics;
using ScreenCapture.Models;
using ScreenCapture.Services.Interop;

namespace ScreenCapture.Services;

/// <summary>Cửa sổ / màn hình bị chụp → <see cref="CaptureInfo"/> (tên chương trình + tiêu đề cho mẫu tên file) và
/// khung màn hình đang có con trỏ (Chụp màn hình hiện tại).
///
/// Cửa sổ bị chụp theo kiểu chụp: Cửa sổ hiện tại = cửa sổ đó; vùng chọn / vùng cố định / cuộn = cửa sổ trên cùng chứa
/// tâm vùng (danh sách cửa sổ lấy CÙNG LÚC chụp ảnh nền đứng yên - lúc chọn vùng overlay của app phủ kín màn hình nên
/// WindowFromPoint chỉ trả về overlay); toàn màn hình / màn hình hiện tại = cửa sổ đang active, là cửa sổ của app thì cửa
/// sổ trên cùng dưới con trỏ.</summary>
public static class CaptureTarget
{
    public static CaptureInfo Describe(IntPtr hwnd, CaptureMode mode) =>
        new(DateTime.Now, mode, AppName(hwnd), WindowTitle(hwnd));

    /// <summary>Cửa sổ trên cùng (z-order của <paramref name="windows"/>) chứa điểm (x, y); Zero nếu không có (desktop).</summary>
    public static IntPtr WindowAt(IReadOnlyList<(IntPtr Hwnd, RECT Rect)> windows, int x, int y) =>
        windows.FirstOrDefault(w => x >= w.Rect.Left && x < w.Rect.Right && y >= w.Rect.Top && y < w.Rect.Bottom).Hwnd;

    public static IntPtr WindowAtCenter(IReadOnlyList<(IntPtr Hwnd, RECT Rect)> windows, RECT area) =>
        WindowAt(windows, (area.Left + area.Right) / 2, (area.Top + area.Bottom) / 2);

    /// <summary>Cửa sổ đang active; là cửa sổ của chính app (launcher / Editor vừa thu nhỏ), không có, hoặc tâm của nó nằm
    /// ngoài <paramref name="captured"/> (vd Màn hình hiện tại chụp màn có chuột mà cửa sổ active ở màn kia - đã gặp
    /// 2026-10-07: ảnh VS Code nhưng tên file "explorer") thì cửa sổ trên cùng dưới con trỏ.</summary>
    public static IntPtr ActiveOrUnderCursor(RECT? captured = null)
    {
        var windows = WindowEnumerator.GetVisibleWindows(); // đã bỏ cửa sổ của app, thu nhỏ, ẩn
        var foreground = NativeMethods.GetForegroundWindow();
        foreach (var (hwnd, rect) in windows)
        {
            if (hwnd == foreground)
            {
                int cx = (rect.Left + rect.Right) / 2, cy = (rect.Top + rect.Bottom) / 2;
                if (captured is not { } area || (cx >= area.Left && cx < area.Right && cy >= area.Top && cy < area.Bottom))
                {
                    return foreground;
                }
                break;
            }
        }
        return NativeMethods.GetCursorPos(out var cursor) ? WindowAt(windows, cursor.X, cursor.Y) : IntPtr.Zero;
    }

    /// <summary>Khung (pixel thật, toạ độ màn hình ảo) của màn hình đang có con trỏ chuột.</summary>
    public static RECT MonitorAtCursor()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        return NativeMethods.GetMonitorInfo(monitor, ref info) ? info.rcMonitor : default;
    }

    /// <summary>Tên tiến trình của cửa sổ (EXCEL, msedge...); rỗng nếu không có cửa sổ / không đọc được. App Store (UWP)
    /// chạy trong khung ApplicationFrameHost → lấy tiến trình của cửa sổ nội dung bên trong.</summary>
    public static string AppName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return string.Empty;
        }
        string name = ProcessName(hwnd);
        if (string.Equals(name, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
        {
            for (var child = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, null, null); child != IntPtr.Zero;
                 child = NativeMethods.FindWindowEx(hwnd, child, null, null))
            {
                string inner = ProcessName(child);
                if (inner.Length > 0 && !string.Equals(inner, name, StringComparison.OrdinalIgnoreCase))
                {
                    return inner;
                }
            }
        }
        return name;
    }

    private static string ProcessName(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0)
        {
            return string.Empty;
        }
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName; // không cần quyền đọc module - chạy được cả với app chạy quyền admin
        }
        catch (Exception)
        {
            return string.Empty; // tiến trình vừa thoát
        }
    }

    public static unsafe string WindowTitle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return string.Empty;
        }
        char* buffer = stackalloc char[512];
        int length = NativeMethods.GetWindowText(hwnd, buffer, 512);
        return length > 0 ? new string(buffer, 0, length).Trim() : string.Empty;
    }
}
