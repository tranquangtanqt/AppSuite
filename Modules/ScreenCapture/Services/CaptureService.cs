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

    public SKBitmap CaptureRect(RECT rectPx)
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
}
