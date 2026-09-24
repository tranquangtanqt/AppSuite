using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using ScreenCapture.Services.Interop;
using SkiaSharp;

namespace ScreenCapture.Services;

/// <summary>
/// Icon ở khay hệ thống (Win32 <c>Shell_NotifyIcon</c>) gắn vào HWND launcher, để app chạy ngầm khi
/// cửa sổ chính bị ẩn - phím tắt toàn cục vẫn hoạt động. Click chuột trái → <see cref="OpenRequested"/>;
/// chuột phải → menu Win32 (<see cref="ShowMenu"/>). Thông báo chuột từ khay tới HWND qua message
/// <c>WM_APP+1</c>, bắt bằng window subclass giống HotkeyService (WinUI 3 không có WndProc).
/// Explorer khởi động lại (message "TaskbarCreated") → tự thêm lại icon.
/// </summary>
public sealed unsafe class TrayIconService : IDisposable
{
    private const nuint SubclassId = 0x5449; // "TI"
    private const uint IconId = 1;
    private const uint CallbackMessage = NativeMethods.WM_APP + 1;
    private static readonly uint TaskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
    private static readonly Dictionary<IntPtr, TrayIconService> Instances = [];

    private readonly IntPtr _hwnd;
    private readonly DispatcherQueue _dispatcher;
    private readonly string _tooltip;
    private IntPtr _icon;
    private bool _visible;

    /// <summary>Click chuột trái vào icon.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>Click chuột phải vào icon - launcher dựng menu và gọi <see cref="ShowMenu"/>.</summary>
    public event EventHandler? MenuRequested;

    public TrayIconService(IntPtr hwnd, DispatcherQueue dispatcher, string tooltip)
    {
        _hwnd = hwnd;
        _dispatcher = dispatcher;
        _tooltip = tooltip;
        _icon = CreateAppIcon(32);
        Instances[hwnd] = this;
        NativeMethods.SetWindowSubclass(hwnd, &SubclassProc, SubclassId, 0);
    }

    public bool IsVisible
    {
        get => _visible;
        set
        {
            if (value == _visible)
            {
                return;
            }
            var data = CreateData(NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP);
            bool ok = NativeMethods.Shell_NotifyIcon(value ? NativeMethods.NIM_ADD : NativeMethods.NIM_DELETE, ref data);
            _visible = ok ? value : _visible;
        }
    }

    /// <summary>Bong bóng thông báo nhỏ cạnh icon (vd lần đầu ẩn xuống khay).</summary>
    public void ShowBalloon(string title, string message)
    {
        if (!_visible)
        {
            return;
        }
        var data = CreateData(NativeMethods.NIF_INFO);
        data.dwInfoFlags = NativeMethods.NIIF_INFO;
        CopyString(message, data.szInfo, 256);
        CopyString(title, data.szInfoTitle, 64);
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    /// <summary>Hiện menu chuột phải tại vị trí con trỏ; trả về id mục được chọn (0 = bấm ra ngoài).</summary>
    public int ShowMenu(IEnumerable<(int Id, string? Text, bool Enabled)> items)
    {
        var menu = NativeMethods.CreatePopupMenu();
        try
        {
            foreach (var (id, text, enabled) in items)
            {
                if (text is null)
                {
                    NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, null);
                }
                else
                {
                    NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING | (enabled ? 0 : NativeMethods.MF_GRAYED), (nuint)id, text);
                }
            }
            NativeMethods.GetCursorPos(out var point);
            // Bắt buộc theo tài liệu TrackPopupMenu: không SetForegroundWindow thì menu không tự đóng
            // khi bấm ra ngoài; PostMessage(WM_NULL) sau đó để lần mở sau hoạt động đúng.
            NativeMethods.SetForegroundWindow(_hwnd);
            int selected = NativeMethods.TrackPopupMenuEx(menu,
                NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_NONOTIFY,
                point.X, point.Y, _hwnd, IntPtr.Zero);
            NativeMethods.PostMessage(_hwnd, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            return selected;
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        IsVisible = false;
        NativeMethods.RemoveWindowSubclass(_hwnd, &SubclassProc, SubclassId);
        Instances.Remove(_hwnd);
        if (_icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }

    private NativeMethods.NOTIFYICONDATAW CreateData(uint flags)
    {
        var data = new NativeMethods.NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NativeMethods.NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };
        CopyString(_tooltip, data.szTip, 128);
        return data;
    }

    private static void CopyString(string value, char* destination, int capacity)
    {
        int length = Math.Min(value.Length, capacity - 1);
        for (int i = 0; i < length; i++)
        {
            destination[i] = value[i];
        }
        destination[length] = '\0';
    }

    /// <summary>Vẽ icon (camera trắng trên nền bo góc #D86445, cùng tông launcher) bằng Skia rồi tạo
    /// HICON từ PNG - CreateIconFromResourceEx nhận thẳng dữ liệu PNG (định dạng icon Vista+), khỏi cần
    /// file .ico trong project.</summary>
    private static IntPtr CreateAppIcon(int size)
    {
        using var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var background = new SKPaint { Color = new SKColor(0xD8, 0x64, 0x45), IsAntialias = true };
            canvas.DrawRoundRect(new SKRect(0, 0, size, size), size * 0.22f, size * 0.22f, background);
            using var typeface = SKTypeface.FromFamilyName("Segoe Fluent Icons") ?? SKTypeface.FromFamilyName("Segoe MDL2 Assets");
            using var glyph = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true,
                Typeface = typeface,
                TextSize = size * 0.62f,
                TextAlign = SKTextAlign.Center,
            };
            float y = size / 2f - (glyph.FontMetrics.Ascent + glyph.FontMetrics.Descent) / 2f;
            canvas.DrawText("", size / 2f, y, glyph);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = png.ToArray();
        fixed (byte* p = bytes)
        {
            return NativeMethods.CreateIconFromResourceEx(p, (uint)bytes.Length, true, 0x00030000, size, size, 0);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static IntPtr SubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, nuint id, nuint refData)
    {
        if (Instances.TryGetValue(hwnd, out var service))
        {
            if (msg == CallbackMessage)
            {
                uint mouse = (uint)(lParam.ToInt64() & 0xFFFF);
                if (mouse is NativeMethods.WM_LBUTTONUP or NativeMethods.WM_LBUTTONDBLCLK)
                {
                    service._dispatcher.TryEnqueue(() => service.OpenRequested?.Invoke(service, EventArgs.Empty));
                }
                else if (mouse == NativeMethods.WM_RBUTTONUP)
                {
                    service._dispatcher.TryEnqueue(() => service.MenuRequested?.Invoke(service, EventArgs.Empty));
                }
                return IntPtr.Zero;
            }
            if (msg == TaskbarCreatedMessage && service._visible)
            {
                // Explorer vừa khởi động lại → icon cũ mất, thêm lại.
                service._visible = false;
                service._dispatcher.TryEnqueue(() => service.IsVisible = true);
            }
        }
        return NativeMethods.DefSubclassProc(hwnd, msg, wParam, lParam);
    }
}
