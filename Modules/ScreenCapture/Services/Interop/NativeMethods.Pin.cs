using System.Runtime.InteropServices;

namespace ScreenCapture.Services.Interop;

/// <summary>Cửa sổ ảnh ghim (Views/PinnedImageWindow.cs): cửa sổ Win32 layered tự vẽ bằng UpdateLayeredWindow - độ mờ cả cửa
/// sổ thật sự (nhìn xuyên xuống dưới), ảnh đúng từng pixel.</summary>
internal static partial class NativeMethods
{
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_LAYERED = 0x00080000, WS_EX_TOPMOST = 0x00000008, WS_EX_TOOLWINDOW_U = 0x00000080;
    public const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;
    public const uint CS_DBLCLKS = 0x0008;
    public const uint ULW_ALPHA = 0x2;
    public const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;
    public const uint MF_CHECKED = 0x0008;
    public static readonly IntPtr IDC_SIZEALL = 32646;
    public const uint WM_SETCURSOR = 0x0020;

    [LibraryImport("user32.dll")]
    public static partial IntPtr SetCursor(IntPtr hCursor);

    public const uint WM_DESTROY = 0x0002, WM_CLOSE = 0x0010, WM_KEYDOWN = 0x0100, WM_CONTEXTMENU = 0x007B,
        WM_NCHITTEST = 0x0084, WM_NCLBUTTONDBLCLK = 0x00A3, WM_NCRBUTTONUP = 0x00A5, WM_NCRBUTTONDOWN = 0x00A4,
        WM_MOUSEWHEEL = 0x020A, WM_DPICHANGED = 0x02E0;
    public const int HTCAPTION = 2;
    public const int VK_CONTROL_KEY = 0x11, VK_C = 0x43;
    public const int MK_CONTROL = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW")]
    public static partial ushort RegisterClassEx(ref WNDCLASSEXW lpwcx);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    public static partial IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    public static partial IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    public static partial IntPtr GetModuleHandle(IntPtr lpModuleName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc,
        ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [LibraryImport("user32.dll")]
    public static partial short GetKeyState(int nVirtKey);
}
