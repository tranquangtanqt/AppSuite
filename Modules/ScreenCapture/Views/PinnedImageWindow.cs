using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using ScreenCapture.Models;
using ScreenCapture.Services;
using ScreenCapture.Services.Interop;
using SkiaSharp;

namespace ScreenCapture.Views;

/// <summary>
/// Ảnh ghim nổi trên màn hình (2026-11-10): cửa sổ Win32 layered luôn ở trên, không có thanh tiêu đề, viền mỏng màu chủ đạo,
/// tự vẽ bằng UpdateLayeredWindow - độ mờ cả cửa sổ (nhìn xuyên xuống dưới) và ảnh đúng từng pixel ở 100%.
/// Kéo chuột trái = di chuyển (Windows tự kéo nhờ WM_NCHITTEST → HTCAPTION); lăn chuột = phóng to / thu nhỏ quanh con trỏ;
/// Ctrl + lăn = độ mờ; chuột phải = menu; bấm đúp / Esc = đóng; Ctrl+C = copy. Không hiện ở taskbar / Alt+Tab.
/// Hiện không lấy focus (đang gõ ở app khác vẫn gõ tiếp); bấm vào ảnh thì ảnh nhận phím.
/// </summary>
public sealed unsafe class PinnedImageWindow
{
    private const string ClassName = "ScreenCapturePinnedImage";
    private static readonly ILogger Log = AppLog.For(nameof(PinnedImageWindow));
    private static readonly Dictionary<IntPtr, PinnedImageWindow> Instances = [];
    private static bool _classRegistered;
    private static readonly SKColor BorderColor = new(0xD8, 0x64, 0x45); // màu chủ đạo (App.xaml SystemAccentColor)

    private enum MenuId : uint { Opacity100 = 1, Opacity80, Opacity60, Opacity40, ActualSize, Copy, OpenEditor, Close, CloseAll }

    private readonly SKBitmap _image;
    private readonly DispatcherQueue _dispatcher;
    private readonly IntPtr _hwnd;
    private double _zoom;
    private int _opacity = PinGeometry.MaxOpacity;
    private PinRect _bounds;
    private bool _closed;

    /// <summary>Menu Copy ảnh / Ctrl+C.</summary>
    public Action<SKBitmap>? CopyRequested { get; init; }

    /// <summary>Menu Mở trong Editor.</summary>
    public Action<SKBitmap>? OpenEditorRequested { get; init; }

    /// <summary>Menu Đóng tất cả ảnh ghim.</summary>
    public Action? CloseAllRequested { get; init; }

    /// <summary>Số ảnh đang ghim (để ghi "Đóng tất cả ảnh ghim (n)").</summary>
    public Func<int>? CountProvider { get; init; }

    public event EventHandler? Closed;

    public IntPtr Handle => _hwnd;

    public double Zoom => _zoom;

    public int OpacityPercent => _opacity;

    /// <param name="image">Ảnh (cửa sổ giữ bản riêng - người gọi vẫn dùng / huỷ ảnh của mình được).</param>
    /// <param name="captured">Vùng vừa chụp trên màn hình (px thật) - ghim đè đúng chỗ; null = giữa màn hình có con trỏ.</param>
    public PinnedImageWindow(SKBitmap image, PinRect? captured, DispatcherQueue dispatcher)
    {
        _image = image.Copy() ?? throw new InvalidOperationException("Không chép được ảnh để ghim.");
        _dispatcher = dispatcher;
        EnsureClass();

        var (window, zoom) = PinGeometry.Initial(_image.Width, _image.Height, captured, WorkAreaAtCursor());
        _zoom = zoom;
        _bounds = window;
        _hwnd = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_TOOLWINDOW_U,
            ClassName, "ScreenCapture - ảnh ghim", NativeMethods.WS_POPUP,
            window.X, window.Y, window.Width, window.Height, IntPtr.Zero, IntPtr.Zero, NativeMethods.GetModuleHandle(IntPtr.Zero), IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Không tạo được cửa sổ ghim (lỗi {Marshal.GetLastPInvokeError()}).");
        }
        Instances[_hwnd] = this;
        Render();
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
    }

    /// <summary>Ẩn tạm (lúc chụp - ảnh ghim không lọt vào ảnh chụp mới) / hiện lại.</summary>
    public void SetHidden(bool hidden)
    {
        if (!_closed)
        {
            NativeMethods.ShowWindow(_hwnd, hidden ? NativeMethods.SW_HIDE : NativeMethods.SW_SHOWNOACTIVATE);
        }
    }

    public void Close()
    {
        if (!_closed)
        {
            NativeMethods.DestroyWindow(_hwnd); // → WM_DESTROY → Cleanup
        }
    }

    public void SetOpacity(int percent)
    {
        SyncPosition();
        _opacity = Math.Clamp(percent, PinGeometry.MinOpacity, PinGeometry.MaxOpacity);
        Render();
    }

    /// <summary>Đổi zoom, điểm (<paramref name="anchorX"/>, <paramref name="anchorY"/>) trên màn hình đứng yên.</summary>
    public void SetZoom(double zoom, int anchorX, int anchorY)
    {
        SyncPosition();
        _zoom = Math.Clamp(zoom, PinGeometry.MinZoom, PinGeometry.MaxZoom);
        var (w, h) = PinGeometry.WindowSize(_image.Width, _image.Height, _zoom);
        _bounds = PinGeometry.ResizeAround(_bounds, anchorX, anchorY, w, h);
        Render();
    }

    /// <summary>Lấy chỗ hiện tại của cửa sổ (người dùng có thể đã kéo đi) - UpdateLayeredWindow đặt lại vị trí theo _bounds.</summary>
    private void SyncPosition()
    {
        if (NativeMethods.GetWindowRect(_hwnd, out var rect) && rect.Width > 0)
        {
            _bounds = _bounds with { X = rect.Left, Y = rect.Top };
        }
    }

    private void Render()
    {
        if (_closed)
        {
            return;
        }
        int width = _bounds.Width, height = _bounds.Height;
        using var surface = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(surface))
        {
            canvas.Clear(BorderColor);
            var target = new SKRect(PinGeometry.Border, PinGeometry.Border, width - PinGeometry.Border, height - PinGeometry.Border);
            using var paint = new SKPaint { FilterQuality = Math.Abs(_zoom - 1) < 0.001 ? SKFilterQuality.None : SKFilterQuality.Medium, IsAntialias = false };
            // Ảnh trong suốt (nền Trong suốt của Ảnh mới) vẫn đọc được: nền trắng dưới ảnh.
            using (var white = new SKPaint { Color = SKColors.White })
            {
                canvas.DrawRect(target, white);
            }
            canvas.DrawBitmap(_image, target, paint);
        }

        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        var memDc = NativeMethods.CreateCompatibleDC(screenDc);
        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = width,
                biHeight = -height, // từ trên xuống
                biPlanes = 1,
                biBitCount = 32,
                biCompression = NativeMethods.BI_RGB,
            },
        };
        var dib = NativeMethods.CreateDIBSection(screenDc, ref info, NativeMethods.DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
        try
        {
            if (dib == IntPtr.Zero)
            {
                Log.LogError("CreateDIBSection lỗi ({Width}x{Height})", width, height);
                return;
            }
            Buffer.MemoryCopy((void*)surface.GetPixels(), (void*)bits, (long)width * height * 4, (long)width * height * 4);
            var old = NativeMethods.SelectObject(memDc, dib);
            var position = new POINT { X = _bounds.X, Y = _bounds.Y };
            var size = new NativeMethods.SIZE { cx = width, cy = height };
            var source = new POINT();
            var blend = new NativeMethods.BLENDFUNCTION
            {
                BlendOp = NativeMethods.AC_SRC_OVER,
                SourceConstantAlpha = (byte)Math.Round(_opacity * 255 / 100.0),
                AlphaFormat = NativeMethods.AC_SRC_ALPHA,
            };
            if (!NativeMethods.UpdateLayeredWindow(_hwnd, screenDc, ref position, ref size, memDc, ref source, 0, ref blend, NativeMethods.ULW_ALPHA))
            {
                Log.LogError("UpdateLayeredWindow lỗi {Error}", Marshal.GetLastPInvokeError());
            }
            NativeMethods.SelectObject(memDc, old);
        }
        finally
        {
            if (dib != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(dib);
            }
            NativeMethods.DeleteDC(memDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private void ShowMenu(int x, int y)
    {
        var menu = NativeMethods.CreatePopupMenu();
        try
        {
            foreach (var (id, percent) in new[] { (MenuId.Opacity100, 100), (MenuId.Opacity80, 80), (MenuId.Opacity60, 60), (MenuId.Opacity40, 40) })
            {
                NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING | (_opacity == percent ? NativeMethods.MF_CHECKED : 0), (nuint)id,
                    percent == 100 ? "Độ mờ: không mờ (100%)" : $"Độ mờ: {percent}%");
            }
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING | (Math.Abs(_zoom - 1) < 0.001 ? NativeMethods.MF_GRAYED : 0), (nuint)MenuId.ActualSize,
                $"Kích thước gốc (đang {Math.Round(_zoom * 100)}%)");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, (nuint)MenuId.Copy, "Copy ảnh\tCtrl+C");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, (nuint)MenuId.OpenEditor, "Mở trong Editor");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, (nuint)MenuId.Close, "Đóng\tEsc");
            int count = CountProvider?.Invoke() ?? 1;
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING | (count > 1 ? 0 : NativeMethods.MF_GRAYED), (nuint)MenuId.CloseAll,
                $"Đóng tất cả ảnh ghim ({count})");

            NativeMethods.SetForegroundWindow(_hwnd); // không thì menu không tự đóng khi bấm ra ngoài
            int command = NativeMethods.TrackPopupMenuEx(menu, NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD, x, y, _hwnd, IntPtr.Zero);
            Run((MenuId)command);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private void Run(MenuId command)
    {
        switch (command)
        {
            case MenuId.Opacity100: SetOpacity(100); break;
            case MenuId.Opacity80: SetOpacity(80); break;
            case MenuId.Opacity60: SetOpacity(60); break;
            case MenuId.Opacity40: SetOpacity(40); break;
            case MenuId.ActualSize: SetZoom(1, _bounds.X + _bounds.Width / 2, _bounds.Y + _bounds.Height / 2); break;
            case MenuId.Copy: CopyRequested?.Invoke(_image); break;
            case MenuId.OpenEditor: OpenEditorRequested?.Invoke(_image.Copy()); break;
            case MenuId.Close: Close(); break;
            case MenuId.CloseAll: CloseAllRequested?.Invoke(); break;
        }
    }

    private void Cleanup()
    {
        _closed = true;
        Instances.Remove(_hwnd);
        _image.Dispose();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Vùng làm việc (trừ taskbar) của màn hình có con trỏ.</summary>
    private static PinRect WorkAreaAtCursor()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = sizeof(NativeMethods.MONITORINFO) };
        if (NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            var w = info.rcWork;
            return new PinRect(w.Left, w.Top, w.Width, w.Height);
        }
        return new PinRect(0, 0, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN), NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN));
    }

    private static void EnsureClass()
    {
        if (_classRegistered)
        {
            return;
        }
        fixed (char* name = ClassName)
        {
            var wc = new NativeMethods.WNDCLASSEXW
            {
                cbSize = (uint)sizeof(NativeMethods.WNDCLASSEXW),
                style = NativeMethods.CS_DBLCLKS,
                lpfnWndProc = &WndProc,
                hInstance = NativeMethods.GetModuleHandle(IntPtr.Zero),
                lpszClassName = name,
            };
            if (NativeMethods.RegisterClassEx(ref wc) == 0)
            {
                throw new InvalidOperationException($"Không đăng ký được lớp cửa sổ ghim (lỗi {Marshal.GetLastPInvokeError()}).");
            }
        }
        _classRegistered = true;
    }

    private static int LowWord(IntPtr value) => (short)((long)value & 0xFFFF);

    private static int HighWord(IntPtr value) => (short)(((long)value >> 16) & 0xFFFF);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (!Instances.TryGetValue(hwnd, out var self))
            {
                return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
            }
            switch (msg)
            {
                case NativeMethods.WM_NCHITTEST:
                    return NativeMethods.HTCAPTION; // cả ảnh là "thanh tiêu đề": Windows tự kéo
                case NativeMethods.WM_SETCURSOR:
                    NativeMethods.SetCursor(NativeMethods.LoadCursor(IntPtr.Zero, NativeMethods.IDC_SIZEALL)); // kéo được
                    return 1;
                case NativeMethods.WM_NCLBUTTONDBLCLK:
                    self._dispatcher.TryEnqueue(self.Close);
                    return IntPtr.Zero;
                case NativeMethods.WM_NCRBUTTONDOWN:
                case NativeMethods.WM_CONTEXTMENU:
                    return IntPtr.Zero; // không hiện menu hệ thống
                case NativeMethods.WM_NCRBUTTONUP:
                {
                    int x = LowWord(lParam), y = HighWord(lParam);
                    self._dispatcher.TryEnqueue(() => self.ShowMenu(x, y)); // ngoài WndProc: menu có vòng lặp riêng
                    return IntPtr.Zero;
                }
                case NativeMethods.WM_MOUSEWHEEL:
                {
                    int delta = HighWord(wParam);
                    bool ctrl = (LowWord(wParam) & NativeMethods.MK_CONTROL) != 0;
                    int x = LowWord(lParam), y = HighWord(lParam);
                    if (ctrl)
                    {
                        self.SetOpacity(PinGeometry.NextOpacity(self._opacity, delta));
                    }
                    else
                    {
                        self.SetZoom(PinGeometry.NextZoom(self._zoom, delta), x, y);
                    }
                    return IntPtr.Zero;
                }
                case NativeMethods.WM_KEYDOWN:
                    if ((int)wParam == NativeMethods.VK_ESCAPE)
                    {
                        self._dispatcher.TryEnqueue(self.Close);
                        return IntPtr.Zero;
                    }
                    if ((int)wParam == NativeMethods.VK_C && NativeMethods.GetKeyState(NativeMethods.VK_CONTROL_KEY) < 0)
                    {
                        self._dispatcher.TryEnqueue(() => self.CopyRequested?.Invoke(self._image));
                        return IntPtr.Zero;
                    }
                    break;
                case NativeMethods.WM_DPICHANGED:
                    return IntPtr.Zero; // giữ đúng cỡ px khi kéo sang màn hình khác DPI
                case NativeMethods.WM_DESTROY:
                    self.Cleanup();
                    break;
            }
            return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Lỗi xử lý message {Message:X} của ảnh ghim", msg);
            return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
        }
    }
}
