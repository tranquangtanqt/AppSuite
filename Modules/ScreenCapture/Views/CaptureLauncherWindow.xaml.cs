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

        RestorePreviousSession();
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
        MinimizeForCapture(hwnd);
        await Task.Delay(200);

        var rect = _captureService.GetVirtualScreenRect();
        var bitmap = _captureService.CaptureRect(rect);

        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        OpenEditor(bitmap);
    }

    private async void WindowButton_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        MinimizeForCapture(hwnd);
        await Task.Delay(200);

        var rect = await _captureService.GetForegroundWindowRectAsync();
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

        if (rect is null)
        {
            RestoreEditorAfterCancel();
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
        MinimizeForCapture(hwnd);
        await Task.Delay(200);

        var virtualRect = _captureService.GetVirtualScreenRect();
        var frozenScreen = _captureService.CaptureRect(virtualRect);

        var overlay = new RegionOverlayWindow(frozenScreen, virtualRect, isFixed, _lastFixedRegion);
        var selection = await overlay.SelectRegionAsync();

        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

        if (selection is null)
        {
            RestoreEditorAfterCancel();
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

    /// <summary>1 cửa sổ Editor duy nhất (giống PicPick): lần chụp đầu mở Editor, các lần sau thêm tab
    /// mới vào Editor đang mở - ảnh chụp trước không bị mất/che bởi cửa sổ mới.</summary>
    private EditorWindow? _editor;

    private bool _editorWasShownBeforeCapture;

    /// <summary>Thu nhỏ launcher VÀ Editor (nếu đang mở) trước khi chụp - không thì lần chụp thứ 2 trở
    /// đi sẽ dính luôn cửa sổ Editor vào ảnh.</summary>
    private void MinimizeForCapture(IntPtr launcherHwnd)
    {
        NativeMethods.ShowWindow(launcherHwnd, NativeMethods.SW_MINIMIZE);
        _editorWasShownBeforeCapture = false;
        if (_editor is not null)
        {
            var editorHwnd = WindowNative.GetWindowHandle(_editor);
            _editorWasShownBeforeCapture = true;
            NativeMethods.ShowWindow(editorHwnd, NativeMethods.SW_MINIMIZE);
        }
    }

    /// <summary>Huỷ chụp (không chọn vùng / không thấy cửa sổ) → mở lại Editor đã thu nhỏ ở trên.
    /// Chụp thành công thì EditorWindow.AddCapture tự mở lại.</summary>
    private void RestoreEditorAfterCancel()
    {
        if (_editor is not null && _editorWasShownBeforeCapture)
        {
            NativeMethods.ShowWindow(WindowNative.GetWindowHandle(_editor), NativeMethods.SW_RESTORE);
        }
    }

    private readonly SessionService _session = new();

    /// <summary>Mở app: phiên trước còn tab (ảnh chụp chưa đóng) → mở lại Editor với đúng các tab đó.</summary>
    private void RestorePreviousSession()
    {
        var (documents, activeId) = _session.Load();
        if (documents.Count == 0)
        {
            return;
        }
        _editor = CreateEditor(documents, activeId, capture: null);
        // Kích hoạt sau launcher (App.OnLaunched Activate launcher sau constructor) để Editor nằm trên.
        DispatcherQueue.TryEnqueue(() => _editor?.Activate());
    }

    private EditorWindow CreateEditor(IReadOnlyList<SessionDocument> restored, Guid? activeId, SKBitmap? capture)
    {
        var editor = new EditorWindow(_fileService, _clipboardService, _session, restored, activeId, capture);
        editor.Closed += (_, _) => _editor = null;
        return editor;
    }

    private void OpenEditor(SKBitmap bitmap)
    {
        if (_editor is null)
        {
            // Chưa mở Editor: vẫn nạp lại tab của phiên trước (nếu có) để ảnh cũ không bị ghi đè mất.
            var (documents, activeId) = _session.Load();
            _editor = CreateEditor(documents, activeId, bitmap);
        }
        else
        {
            _editor.AddCapture(bitmap);
        }
        _editor.Activate();
    }
}
