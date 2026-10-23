using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace MainLauncher.Services;

/// <summary>
/// Icon MainLauncher ở khay hệ thống (Win32 Shell_NotifyIcon gắn vào HWND cửa sổ chính) - dùng khi "Khi bấm X: thu xuống khay
/// hệ thống" bật. Chuột trái → <see cref="OpenRequested"/>; chuột phải → <see cref="MenuRequested"/> (gọi <see cref="ShowMenu"/>).
/// Thông báo chuột tới HWND qua WM_APP+1, bắt bằng window subclass (WinUI 3 không có WndProc) - cùng cách với
/// ScreenCapture\Services\TrayIconService. Explorer khởi động lại ("TaskbarCreated") → tự thêm lại icon.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;
    private const uint WM_APP = 0x8000;
    private const uint CallbackMessage = WM_APP + 1;
    private const uint WM_NULL = 0x0000, WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONUP = 0x0205;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10;
    private const uint NIIF_INFO = 0x1;
    private const uint MF_STRING = 0x0, MF_GRAYED = 0x1, MF_SEPARATOR = 0x800, MF_DEFAULT = 0x1000;
    private const uint TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100, TPM_NONOTIFY = 0x80;
    private const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;
    private const int SM_CXSMICON = 49, SM_CYSMICON = 50;
    private static readonly nuint SubclassId = 0x4D4C; // "ML"
    private static readonly uint TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

    private readonly IntPtr _hwnd;
    private readonly DispatcherQueue _dispatcher;
    private readonly string _tooltip;
    private readonly SubclassProc _subclassProc; // giữ tham chiếu - GC thu delegate là crash
    private IntPtr _icon;
    private bool _visible;

    public event EventHandler? OpenRequested;

    public event EventHandler? MenuRequested;

    public TrayIcon(IntPtr hwnd, DispatcherQueue dispatcher, string tooltip)
    {
        _hwnd = hwnd;
        _dispatcher = dispatcher;
        _tooltip = tooltip;
        _icon = LoadAppIcon();
        _subclassProc = OnWindowMessage;
        SetWindowSubclass(hwnd, _subclassProc, SubclassId, 0);
    }

    public bool IsVisible
    {
        get => _visible;
        set
        {
            if (value == _visible)
                return;
            var data = CreateData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
            if (Shell_NotifyIcon(value ? NIM_ADD : NIM_DELETE, ref data))
                _visible = value;
        }
    }

    /// <summary>Bong bóng thông báo cạnh icon (vd lần đầu thu xuống khay).</summary>
    public void ShowBalloon(string title, string message)
    {
        if (!_visible)
            return;
        var data = CreateData(NIF_INFO);
        data.dwInfoFlags = NIIF_INFO;
        data.szInfo = Truncate(message, 255);
        data.szInfoTitle = Truncate(title, 63);
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    /// <summary>Menu chuột phải tại con trỏ; (Id, Text = null là đường kẻ, Enabled, IsDefault in đậm). Trả id được chọn, 0 = bấm ra ngoài.</summary>
    public int ShowMenu(IEnumerable<(int Id, string? Text, bool Enabled, bool IsDefault)> items)
    {
        var menu = CreatePopupMenu();
        try
        {
            foreach (var (id, text, enabled, isDefault) in items)
            {
                if (text is null)
                    AppendMenu(menu, MF_SEPARATOR, 0, null);
                else
                    AppendMenu(menu, MF_STRING | (enabled ? 0 : MF_GRAYED) | (isDefault ? MF_DEFAULT : 0), (nuint)id, text);
            }
            GetCursorPos(out var point);
            // Theo tài liệu TrackPopupMenu: không SetForegroundWindow thì menu không đóng khi bấm ra ngoài; WM_NULL cho lần sau.
            SetForegroundWindow(_hwnd);
            var selected = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY, point.X, point.Y, _hwnd, IntPtr.Zero);
            PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            return selected;
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        IsVisible = false;
        RemoveWindowSubclass(_hwnd, _subclassProc, SubclassId);
        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }

    private NOTIFYICONDATAW CreateData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = Truncate(_tooltip, 127),
        szInfo = "",
        szInfoTitle = "",
    };

    private static string Truncate(string value, int max) => value.Length > max ? value[..max] : value;

    /// <summary>Assets\MainLauncher.ico cạnh exe (cỡ icon nhỏ của hệ thống, đúng DPI); thiếu file thì lấy icon trong exe.</summary>
    private static IntPtr LoadAppIcon()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "Assets", "MainLauncher.ico");
        if (File.Exists(file))
        {
            var icon = LoadImage(IntPtr.Zero, file, IMAGE_ICON, GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), LR_LOADFROMFILE);
            if (icon != IntPtr.Zero)
                return icon;
        }
        var small = new IntPtr[1];
        return ExtractIconEx(Environment.ProcessPath ?? "", 0, null, small, 1) > 0 ? small[0] : IntPtr.Zero;
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, nuint id, nuint refData)
    {
        if (msg == CallbackMessage)
        {
            var mouse = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouse is WM_LBUTTONUP or WM_LBUTTONDBLCLK)
                _dispatcher.TryEnqueue(() => OpenRequested?.Invoke(this, EventArgs.Empty));
            else if (mouse == WM_RBUTTONUP)
                _dispatcher.TryEnqueue(() => MenuRequested?.Invoke(this, EventArgs.Empty));
            return IntPtr.Zero;
        }
        if (msg == TaskbarCreatedMessage && _visible)
        {
            _visible = false; // Explorer vừa khởi động lại → icon cũ mất, thêm lại
            _dispatcher.TryEnqueue(() => IsVisible = true);
        }
        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    // ----- Win32 -----

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, nuint dwRefData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, nuint dwRefData);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, nuint uIdSubclass);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATAW lpData);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string lpString);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
