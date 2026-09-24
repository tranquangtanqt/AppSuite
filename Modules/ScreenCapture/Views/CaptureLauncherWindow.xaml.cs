using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScreenCapture.Models;
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

    private readonly SettingsStore _settingsStore = new();
    private AppSettings _settings;
    private readonly HotkeyService _hotkeys;
    private IReadOnlyList<HotkeyBinding> _failedHotkeys = [];
    private SettingsWindow? _settingsWindow;

    /// <summary>Đang trong 1 lần chụp (chờ hẹn giờ / overlay chọn vùng) - bỏ qua phím tắt bấm chồng.</summary>
    private bool _isCapturing;

    // "Chụp lại lần gần nhất": nhớ kiểu chụp + vùng (toạ độ màn hình) của lần chụp thành công gần nhất.
    private enum LastCaptureKind { None, FullScreen, ActiveWindow, Rect }
    private LastCaptureKind _lastKind;
    private RECT _lastRect;

    public CaptureLauncherWindow()
    {
        InitializeComponent();

        // Kích thước vừa đủ cho lưới thẻ 2 cột - AppWindow.Resize nhận pixel vật lý nên nhân theo DPI.
        var hwnd = WindowNative.GetWindowHandle(this);
        var scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(720 * scale), (int)(520 * scale)));

        _settings = _settingsStore.Load();
        _session.ApplySettings(_settings);

        _hotkeys = new HotkeyService(hwnd, DispatcherQueue);
        _hotkeys.Pressed += Hotkeys_Pressed;
        _failedHotkeys = _hotkeys.Apply(_settings.Hotkeys);
        ReportFailedHotkeys();
        Closed += (_, _) => _hotkeys.Dispose();

        RestorePreviousSession();
    }

    private void ShowStatus(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }

    private void ReportFailedHotkeys()
    {
        if (_failedHotkeys.Count > 0)
        {
            ShowStatus($"Phím tắt đang bị Windows/app khác giữ: {string.Join(", ", _failedHotkeys.Select(h => h.Describe()))}. Đổi trong Cài đặt → Phím tắt.",
                InfoBarSeverity.Warning);
        }
    }

    // ---- Cài đặt ----

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings.Clone(), _failedHotkeys.Select(h => h.Action), _fileService, ApplySettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Activate();
    }

    /// <summary>Bấm OK ở cửa sổ Cài đặt: lưu file, đăng ký lại phím tắt, áp giới hạn phiên. Trả về phím
    /// tắt đăng ký lỗi để cửa sổ Cài đặt đánh dấu ⚠.</summary>
    private IReadOnlyList<HotkeyBinding> ApplySettings(AppSettings settings)
    {
        _settings = settings;
        try
        {
            _settingsStore.Save(settings);
        }
        catch (Exception ex)
        {
            ShowStatus($"Không lưu được cài đặt: {ex.Message}", InfoBarSeverity.Error);
        }
        _session.ApplySettings(settings);
        _editor?.PersistSession(); // tắt "nhớ tab" → dọn thư mục tạm ngay; đổi giới hạn → áp ngay
        _failedHotkeys = _hotkeys.Apply(settings.Hotkeys);
        StatusInfoBar.IsOpen = false;
        ReportFailedHotkeys();
        return _failedHotkeys;
    }

    // ---- Chụp ----

    private void Hotkeys_Pressed(object? sender, HotkeyAction action)
    {
        _ = action switch
        {
            HotkeyAction.FullScreen => CaptureFullScreenAsync(),
            HotkeyAction.ActiveWindow => CaptureActiveWindowAsync(),
            HotkeyAction.Region => CaptureRegionAsync(isFixed: false),
            HotkeyAction.FixedRegion => CaptureRegionAsync(isFixed: true),
            HotkeyAction.RepeatLast => RepeatLastCaptureAsync(),
            _ => Task.CompletedTask,
        };
    }

    private async void FullScreenButton_Click(object sender, RoutedEventArgs e) => await CaptureFullScreenAsync();
    private async void WindowButton_Click(object sender, RoutedEventArgs e) => await CaptureActiveWindowAsync();
    private async void RegionButton_Click(object sender, RoutedEventArgs e) => await CaptureRegionAsync(isFixed: false);
    private async void FixedRegionButton_Click(object sender, RoutedEventArgs e) => await CaptureRegionAsync(isFixed: true);

    private async Task CaptureFullScreenAsync()
    {
        if (!await BeginCaptureAsync())
        {
            return;
        }
        try
        {
            var bitmap = _captureService.CaptureRect(_captureService.GetVirtualScreenRect());
            _lastKind = LastCaptureKind.FullScreen;
            await FinishCaptureAsync(bitmap);
        }
        finally
        {
            _isCapturing = false;
        }
    }

    private async Task CaptureActiveWindowAsync()
    {
        if (!await BeginCaptureAsync())
        {
            return;
        }
        try
        {
            var rect = await _captureService.GetForegroundWindowRectAsync();
            if (rect is null)
            {
                CancelCapture("Không tìm thấy cửa sổ để chụp.", InfoBarSeverity.Warning);
                return;
            }
            var bitmap = _captureService.CaptureRect(rect.Value);
            _lastKind = LastCaptureKind.ActiveWindow;
            await FinishCaptureAsync(bitmap);
        }
        finally
        {
            _isCapturing = false;
        }
    }

    private async Task CaptureRegionAsync(bool isFixed)
    {
        if (!await BeginCaptureAsync())
        {
            return;
        }
        try
        {
            var virtualRect = _captureService.GetVirtualScreenRect();
            var frozenScreen = _captureService.CaptureRect(virtualRect);

            var overlay = new RegionOverlayWindow(frozenScreen, virtualRect, isFixed, _lastFixedRegion);
            var selection = await overlay.SelectRegionAsync();
            if (selection is null)
            {
                CancelCapture("Đã huỷ chọn vùng.");
                return;
            }

            if (isFixed)
            {
                _lastFixedRegion = selection.Value;
            }
            _lastKind = LastCaptureKind.Rect;
            _lastRect = selection.Value;

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
            await FinishCaptureAsync(cropped);
        }
        finally
        {
            _isCapturing = false;
        }
    }

    /// <summary>Chụp lại đúng kiểu của lần gần nhất; vùng chọn / vùng cố định thì chụp lại đúng vùng đó
    /// luôn, không hiện overlay (giống "Repeat Last Capture" của PicPick).</summary>
    private async Task RepeatLastCaptureAsync()
    {
        switch (_lastKind)
        {
            case LastCaptureKind.FullScreen:
                await CaptureFullScreenAsync();
                break;
            case LastCaptureKind.ActiveWindow:
                await CaptureActiveWindowAsync();
                break;
            case LastCaptureKind.Rect:
                if (!await BeginCaptureAsync())
                {
                    return;
                }
                try
                {
                    await FinishCaptureAsync(_captureService.CaptureRect(_lastRect));
                }
                finally
                {
                    _isCapturing = false;
                }
                break;
            default:
                ShowStatus("Chưa có lần chụp nào để lặp lại.");
                break;
        }
    }

    /// <summary>Ẩn launcher + Editor rồi chờ (200ms cho Windows vẽ lại + hẹn giờ trong Cài đặt).
    /// Trả false nếu đang có 1 lần chụp khác chưa xong.</summary>
    private async Task<bool> BeginCaptureAsync()
    {
        if (_isCapturing)
        {
            return false;
        }
        _isCapturing = true;
        MinimizeForCapture(WindowNative.GetWindowHandle(this));
        await Task.Delay(200 + Math.Clamp(_settings.CaptureDelaySeconds, 0, 10) * 1000);
        return true;
    }

    private void CancelCapture(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        RestoreLauncherAfterCapture();
        RestoreEditorAfterCancel();
        ShowStatus(message, severity);
    }

    /// <summary>Mở ảnh trong Editor rồi chạy các tuỳ chọn sau khi chụp (tự lưu, tự copy).</summary>
    private async Task FinishCaptureAsync(SKBitmap bitmap)
    {
        RestoreLauncherAfterCapture();
        OpenEditor(bitmap);

        var document = _editor?.CurrentDocument;
        if (_settings.AutoSave && document is not null)
        {
            try
            {
                Directory.CreateDirectory(_settings.AutoSaveFolder);
                document.SaveToFolder(_settings.AutoSaveFolder);
                _editor?.PersistSession(); // ghi lại cờ "đã lưu" vào phiên tạm
            }
            catch (Exception ex)
            {
                document.StatusText = $"Tự động lưu thất bại: {ex.Message}";
            }
        }
        if (_settings.CopyToClipboardAfterCapture)
        {
            try
            {
                await _clipboardService.CopyBitmapAsync(bitmap);
                if (document is not null && !_settings.AutoSave)
                {
                    document.StatusText = "Đã copy ảnh chụp vào clipboard.";
                }
            }
            catch (Exception ex)
            {
                if (document is not null)
                {
                    document.StatusText = $"Không copy được vào clipboard: {ex.Message}";
                }
            }
        }
    }

    /// <summary>1 cửa sổ Editor duy nhất (giống PicPick): lần chụp đầu mở Editor, các lần sau thêm tab
    /// mới vào Editor đang mở - ảnh chụp trước không bị mất/che bởi cửa sổ mới.</summary>
    private EditorWindow? _editor;

    private bool _editorWasShownBeforeCapture;
    private bool _launcherWasMinimized;

    /// <summary>Thu nhỏ launcher VÀ Editor (nếu đang mở) trước khi chụp - không thì lần chụp thứ 2 trở
    /// đi sẽ dính luôn cửa sổ Editor vào ảnh.</summary>
    private void MinimizeForCapture(IntPtr launcherHwnd)
    {
        // Chụp bằng phím tắt khi launcher đang thu nhỏ → chụp xong giữ nguyên thu nhỏ, không bật lên.
        _launcherWasMinimized = NativeMethods.IsIconic(launcherHwnd);
        NativeMethods.ShowWindow(launcherHwnd, NativeMethods.SW_MINIMIZE);
        _editorWasShownBeforeCapture = false;
        if (_editor is not null)
        {
            var editorHwnd = WindowNative.GetWindowHandle(_editor);
            _editorWasShownBeforeCapture = true;
            NativeMethods.ShowWindow(editorHwnd, NativeMethods.SW_MINIMIZE);
        }
    }

    private void RestoreLauncherAfterCapture()
    {
        if (!_launcherWasMinimized)
        {
            NativeMethods.ShowWindow(WindowNative.GetWindowHandle(this), NativeMethods.SW_RESTORE);
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
        editor.SettingsRequested += (_, _) => OpenSettings();
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
