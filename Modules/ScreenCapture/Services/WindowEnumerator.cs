using System.Runtime.InteropServices;
using ScreenCapture.Services.Interop;

namespace ScreenCapture.Services;

/// <summary>Khung các cửa sổ đang thấy trên màn hình, theo thứ tự trên → dưới (z-order của EnumWindows) -
/// overlay Vùng chọn dùng để tô viền cửa sổ dưới con trỏ và chụp cả cửa sổ khi click.
///
/// Bỏ: cửa sổ ẩn / thu nhỏ / bị "cloak" (cửa sổ UWP nền, cửa sổ ở desktop ảo khác), cửa sổ trong suốt với
/// chuột (overlay của driver đồ hoạ, ...), tool window không tiêu đề, desktop (Progman/WorkerW) và cửa sổ của
/// chính app. Khung lấy theo DWM extended frame bounds (không gồm bóng đổ) như "Cửa sổ hiện tại".</summary>
public static class WindowEnumerator
{
    [ThreadStatic] private static List<IntPtr>? _collected;

    public static unsafe List<RECT> GetVisibleWindowRects()
    {
        _collected = [];
        try
        {
            NativeMethods.EnumWindows(&Collect, IntPtr.Zero);
            uint ownPid = (uint)Environment.ProcessId;
            var result = new List<RECT>();
            foreach (var hWnd in _collected)
            {
                if (IsCandidate(hWnd, ownPid) && TryGetBounds(hWnd, out var rect))
                {
                    result.Add(rect);
                }
            }
            return result;
        }
        finally
        {
            _collected = null;
        }
    }

    [UnmanagedCallersOnly]
    private static int Collect(IntPtr hWnd, IntPtr lParam)
    {
        _collected?.Add(hWnd);
        return 1; // tiếp tục liệt kê
    }

    private static unsafe bool IsCandidate(IntPtr hWnd, uint ownPid)
    {
        if (!NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd))
        {
            return false;
        }
        NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
        if (pid == ownPid)
        {
            return false;
        }
        if (NativeMethods.DwmGetWindowAttributeInt(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return false;
        }
        long exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE);
        if ((exStyle & NativeMethods.WS_EX_TRANSPARENT) != 0
            || ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0 && NativeMethods.GetWindowTextLength(hWnd) == 0))
        {
            return false;
        }
        char* className = stackalloc char[64];
        int length = NativeMethods.GetClassName(hWnd, className, 64);
        var name = new string(className, 0, length);
        return name is not ("Progman" or "WorkerW");
    }

    private static bool TryGetBounds(IntPtr hWnd, out RECT rect)
    {
        if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>()) != 0
            && !NativeMethods.GetWindowRect(hWnd, out rect))
        {
            return false;
        }
        return rect.Right - rect.Left > 0 && rect.Bottom - rect.Top > 0;
    }
}
