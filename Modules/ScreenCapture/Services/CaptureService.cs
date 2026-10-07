using ScreenCapture.Services.Interop;
using SkiaSharp;

namespace ScreenCapture.Services;

/// <inheritdoc cref="ICaptureService"/>
public sealed class CaptureService : ICaptureService
{
    public RECT GetVirtualScreenRect()
    {
        int left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        return new RECT { Left = left, Top = top, Right = left + width, Bottom = top + height };
    }

    public async Task<RECT?> GetForegroundWindowRectAsync()
    {
        var hWnd = NativeMethods.GetForegroundWindow();
        if (hWnd == IntPtr.Zero)
        {
            return null;
        }

        if (NativeMethods.IsIconic(hWnd))
        {
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
        }
        NativeMethods.SetForegroundWindow(hWnd);

        // Give the target window time to repaint/finish any show/restore animation before capture.
        await Task.Delay(200);

        if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                out var dwmRect, System.Runtime.InteropServices.Marshal.SizeOf<RECT>()) == 0)
        {
            return dwmRect;
        }

        return NativeMethods.GetWindowRect(hWnd, out var rect) ? rect : null;
    }

    public SKBitmap CaptureRect(RECT rectPx, bool includeCursor = false)
    {
        int width = rectPx.Width;
        int height = rectPx.Height;
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("Capture rect must have positive width/height.", nameof(rectPx));
        }

        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
        IntPtr bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, width, height);
        IntPtr oldBitmap = NativeMethods.SelectObject(memDc, bitmap);
        try
        {
            NativeMethods.BitBlt(memDc, 0, 0, width, height, screenDc, rectPx.Left, rectPx.Top, NativeMethods.SRCCOPY);
            if (includeCursor)
            {
                DrawCursor(memDc, rectPx);
            }

            var info = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height, // negative = top-down DIB, matches SKBitmap row order
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = NativeMethods.BI_RGB,
                }
            };

            var skBitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            IntPtr pixels = skBitmap.GetPixels();
            NativeMethods.GetDIBits(memDc, bitmap, 0, (uint)height, pixels, ref info, NativeMethods.DIB_RGB_COLORS);
            if (includeCursor)
            {
                MakeOpaque(pixels, width * height); // DrawIconEx có thể để alpha = 0 ở chỗ vẽ con trỏ → lỗ trong suốt
            }
            return skBitmap;
        }
        finally
        {
            NativeMethods.SelectObject(memDc, oldBitmap);
            NativeMethods.DeleteObject(bitmap);
            NativeMethods.DeleteDC(memDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>Ảnh chụp màn hình luôn đục: đặt alpha = 255 cho mọi pixel BGRA.</summary>
    private static unsafe void MakeOpaque(IntPtr pixels, int count)
    {
        uint* p = (uint*)pixels;
        for (int i = 0; i < count; i++)
        {
            p[i] |= 0xFF000000;
        }
    }

    /// <summary>BitBlt không chụp con trỏ (Windows vẽ nó riêng) → vẽ lại con trỏ hiện tại lên ảnh: vị trí = toạ độ con trỏ
    /// trừ điểm nóng (hotspot, vd đầu mũi tên) của nó, cỡ gốc của con trỏ. Con trỏ đang ẩn (gõ chữ) / nằm ngoài vùng → bỏ.</summary>
    private static void DrawCursor(IntPtr memDc, RECT rectPx)
    {
        var info = new NativeMethods.CURSORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.CURSORINFO>() };
        if (!NativeMethods.GetCursorInfo(ref info) || (info.flags & NativeMethods.CURSOR_SHOWING) == 0 || info.hCursor == IntPtr.Zero)
        {
            return;
        }
        int hotX = 0, hotY = 0;
        if (NativeMethods.GetIconInfo(info.hCursor, out var icon))
        {
            hotX = icon.xHotspot;
            hotY = icon.yHotspot;
            // GetIconInfo tạo bản sao 2 bitmap - phải xoá.
            if (icon.hbmMask != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(icon.hbmMask);
            }
            if (icon.hbmColor != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(icon.hbmColor);
            }
        }
        int x = info.ptScreenPos.X - rectPx.Left - hotX;
        int y = info.ptScreenPos.Y - rectPx.Top - hotY;
        if (info.ptScreenPos.X < rectPx.Left || info.ptScreenPos.X >= rectPx.Right
            || info.ptScreenPos.Y < rectPx.Top || info.ptScreenPos.Y >= rectPx.Bottom)
        {
            return;
        }
        NativeMethods.DrawIconEx(memDc, x, y, info.hCursor, 0, 0, 0, IntPtr.Zero, NativeMethods.DI_NORMAL);
    }
}
