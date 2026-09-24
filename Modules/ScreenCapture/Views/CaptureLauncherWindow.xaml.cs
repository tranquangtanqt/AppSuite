using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScreenCapture.Services;
using ScreenCapture.Services.Interop;
using SkiaSharp;
using Windows.Graphics;
using WinRT.Interop;

namespace ScreenCapture.Views;

/// <summary>Main entry window: 4 capture-mode cards (+ Scroll card, disabled). Owns opening the overlay/editor windows (View
/// responsibility, same split CsvEditor's MainWindow uses) - no ViewModel here since there is no state
/// beyond "which button was clicked".</summary>
public sealed partial class CaptureLauncherWindow : Window
{
    private readonly ICaptureService _captureService = new CaptureService();
    private readonly IImageFileService _fileService = new ImageFileService();
    private readonly IClipboardService _clipboardService = new ClipboardService();

    /// <summary>Session-only remembered Fixed Region rect (PLAN.md: mặc định session-only, cần xác
    /// nhận lại với người dùng nếu cần lưu qua lần restart).</summary>
    private RECT? _lastFixedRegion;

    public CaptureLauncherWindow()
    {
        InitializeComponent();

        // Kích thước vừa đủ cho lưới thẻ 2 cột - AppWindow.Resize nhận pixel vật lý nên nhân theo DPI.
        var scale = NativeMethods.GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(720 * scale), (int)(500 * scale)));
    }

    private void ShowStatus(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }

    private async void FullScreenButton_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE);
        await Task.Delay(200);

        var rect = _captureService.GetVirtualScreenRect();
        var bitmap = _captureService.CaptureRect(rect);

        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        OpenEditor(bitmap);
    }

    private async void WindowButton_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE);
        await Task.Delay(200);

        var rect = await _captureService.GetForegroundWindowRectAsync();
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

        if (rect is null)
        {
            ShowStatus("Không tìm thấy cửa sổ để chụp.", InfoBarSeverity.Warning);
            return;
        }

        var bitmap = _captureService.CaptureRect(rect.Value);
        OpenEditor(bitmap);
    }

    private async void RegionButton_Click(object sender, RoutedEventArgs e) => await StartRegionCaptureAsync(isFixed: false);

    private async void FixedRegionButton_Click(object sender, RoutedEventArgs e) => await StartRegionCaptureAsync(isFixed: true);

    private async Task StartRegionCaptureAsync(bool isFixed)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE);
        await Task.Delay(200);

        var virtualRect = _captureService.GetVirtualScreenRect();
        var frozenScreen = _captureService.CaptureRect(virtualRect);

        var overlay = new RegionOverlayWindow(frozenScreen, virtualRect, isFixed, _lastFixedRegion);
        var selection = await overlay.SelectRegionAsync();

        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

        if (selection is null)
        {
            ShowStatus("Đã huỷ chọn vùng.");
            return;
        }

        if (isFixed)
        {
            _lastFixedRegion = selection.Value;
        }

        var cropRect = new SKRectI(
            selection.Value.Left - virtualRect.Left,
            selection.Value.Top - virtualRect.Top,
            selection.Value.Right - virtualRect.Left,
            selection.Value.Bottom - virtualRect.Top);

        var cropped = new SKBitmap(cropRect.Width, cropRect.Height);
        using (var canvas = new SKCanvas(cropped))
        {
            canvas.DrawBitmap(frozenScreen, cropRect, new SKRect(0, 0, cropRect.Width, cropRect.Height));
        }
        OpenEditor(cropped);
    }

    private void OpenEditor(SKBitmap bitmap)
    {
        var editor = new EditorWindow(bitmap, _fileService, _clipboardService);
        editor.Activate();
    }
}
