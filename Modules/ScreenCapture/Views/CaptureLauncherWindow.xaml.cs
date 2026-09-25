using Microsoft.Extensions.Logging;
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
    private static readonly ILogger Log = AppLog.For(nameof(CaptureLauncherWindow));

    private readonly ICaptureService _captureService = new CaptureService();
    private readonly IImageFileService _fileService = new ImageFileService();
    private readonly IClipboardService _clipboardService = new ClipboardService();

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

    private readonly TrayIconService _tray;
    private bool _exiting;
    private bool _trayBalloonShown;

    /// <summary>App.OnLaunched không Activate cửa sổ khi true (chạy ngầm ở khay ngay từ đầu).</summary>
    public bool StartHidden { get; }

    /// <param name="startInTray">Mở bằng tham số --tray (Khởi động cùng Windows).</param>
    public CaptureLauncherWindow(bool startInTray = false)
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

        _tray = new TrayIconService(hwnd, DispatcherQueue, "ScreenCapture - chụp màn hình");
        _tray.OpenRequested += (_, _) => ShowLauncher();
        _tray.MenuRequested += (_, _) => ShowTrayMenu();
        _tray.IsVisible = _settings.RunInTray;

        // Bấm X: bật "chạy ngầm ở khay" thì chỉ ẩn xuống khay; tắt thì thoát hẳn (hỏi lưu ảnh qua Editor).
        AppWindow.Closing += (sender, args) =>
        {
            if (_exiting)
            {
                return;
            }
            args.Cancel = true;
            if (_settings.RunInTray)
            {
                HideToTray();
            }
            else
            {
                _ = ExitAsync();
            }
        };
        Closed += (_, _) =>
        {
            _hotkeys.Dispose();
            _tray.Dispose();
        };

        StartHidden = startInTray && _settings.RunInTray;
        if (!StartHidden)
        {
            // Chạy ngầm từ lúc khởi động Windows thì không bật Editor lên - tab cũ vẫn được nạp lại ở
            // lần chụp / mở Editor đầu tiên (OpenEditor tự Load phiên).
            RestorePreviousSession();
        }
    }

    // ---- Khay hệ thống ----

    private void HideToTray()
    {
        AppWindow.Hide();
        if (!_trayBalloonShown)
        {
            _trayBalloonShown = true;
            _tray.ShowBalloon("ScreenCapture vẫn đang chạy",
                "Phím tắt vẫn dùng được. Bấm icon ở khay để mở lại, chuột phải để Thoát.");
        }
    }

    private void ShowLauncher()
    {
        AppWindow.Show();
        var hwnd = WindowNative.GetWindowHandle(this);
        if (NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        }
        Activate();
    }

    private enum TrayCommand { FullScreen = 1, ActiveWindow, Region, FixedRegion, OpenLauncher, OpenEditor, Settings, Exit, Scroll, ScrollHorizontal }

    private void ShowTrayMenu()
    {
        bool canOpenEditor = _editor is not null || _session.Load().Documents.Count > 0;
        var command = (TrayCommand)_tray.ShowMenu(
        [
            ((int)TrayCommand.FullScreen, "Chụp toàn màn hình", true),
            ((int)TrayCommand.ActiveWindow, "Chụp cửa sổ hiện tại", true),
            ((int)TrayCommand.Region, "Chụp vùng chọn", true),
            ((int)TrayCommand.FixedRegion, "Chụp vùng cố định", true),
            ((int)TrayCommand.Scroll, "Chụp cuộn dọc", true),
            ((int)TrayCommand.ScrollHorizontal, "Chụp cuộn ngang", true),
            (0, null, true),
            ((int)TrayCommand.OpenLauncher, "Mở cửa sổ chính", true),
            ((int)TrayCommand.OpenEditor, "Mở Editor", canOpenEditor),
            ((int)TrayCommand.Settings, "Cài đặt...", true),
            (0, null, true),
            ((int)TrayCommand.Exit, "Thoát", true),
        ]);

        switch (command)
        {
            case TrayCommand.FullScreen: RunInBackground(CaptureFullScreenAsync); break;
            case TrayCommand.ActiveWindow: RunInBackground(CaptureActiveWindowAsync); break;
            case TrayCommand.Region: RunInBackground(() => CaptureRegionAsync(isFixed: false)); break;
            case TrayCommand.FixedRegion: RunInBackground(() => CaptureRegionAsync(isFixed: true)); break;
            case TrayCommand.Scroll: RunInBackground(() => CaptureScrollAsync(ScrollDirection.Vertical)); break;
            case TrayCommand.ScrollHorizontal: RunInBackground(() => CaptureScrollAsync(ScrollDirection.Horizontal)); break;
            case TrayCommand.OpenLauncher: ShowLauncher(); break;
            case TrayCommand.OpenEditor: OpenExistingEditor(); break;
            case TrayCommand.Settings: OpenSettings(); break;
            case TrayCommand.Exit: _ = ExitAsync(); break;
        }
    }

    private void OpenExistingEditor()
    {
        if (_editor is null)
        {
            var (documents, activeId) = _session.Load();
            if (documents.Count == 0)
            {
                return;
            }
            _editor = CreateEditor(documents, activeId, capture: null);
        }
        var editorHwnd = WindowNative.GetWindowHandle(_editor);
        if (NativeMethods.IsIconic(editorHwnd))
        {
            NativeMethods.ShowWindow(editorHwnd, NativeMethods.SW_RESTORE);
        }
        _editor.Activate();
    }

    /// <summary>Thoát hẳn app: đóng Editor theo đúng luồng của nó (lưu tạm phiên / hỏi lưu ảnh - người
    /// dùng Huỷ thì không thoát), đóng Cài đặt, rồi thoát.</summary>
    private async Task ExitAsync()
    {
        if (_editor is not null && !await _editor.RequestCloseAsync())
        {
            return;
        }
        _settingsWindow?.Close();
        _exiting = true;
        Close();
        Application.Current.Exit();
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
            Log.LogWarning("Phím tắt không đăng ký được (bị Windows/app khác giữ): {Hotkeys}", string.Join(", ", _failedHotkeys.Select(h => $"{h.Action}={h.Describe()}")));
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
        // Cửa sổ Cài đặt không có mục Vùng cố định (ReadSettings tạo AppSettings mới) → giữ vùng đã lưu.
        settings.LastFixedRegion = _settings.LastFixedRegion;
        _settings = settings;
        try
        {
            _settingsStore.Save(settings);
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Không lưu được cài đặt");
            ShowStatus($"Không lưu được cài đặt: {ex.Message}", InfoBarSeverity.Error);
        }
        _session.ApplySettings(settings);
        _editor?.PersistSession(); // tắt "nhớ tab" → dọn thư mục tạm ngay; đổi giới hạn → áp ngay
        _tray.IsVisible = settings.RunInTray;
        try
        {
            StartupRegistration.Apply(settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Không đặt được khởi động cùng Windows");
            ShowStatus($"Không đặt được khởi động cùng Windows: {ex.Message}", InfoBarSeverity.Error);
        }
        _failedHotkeys = _hotkeys.Apply(settings.Hotkeys);
        StatusInfoBar.IsOpen = false;
        ReportFailedHotkeys();
        return _failedHotkeys;
    }

    // ---- Chụp ----

    private void Hotkeys_Pressed(object? sender, HotkeyAction action) => RunInBackground(action switch
    {
        HotkeyAction.FullScreen => CaptureFullScreenAsync,
        HotkeyAction.ActiveWindow => CaptureActiveWindowAsync,
        HotkeyAction.Region => () => CaptureRegionAsync(isFixed: false),
        HotkeyAction.FixedRegion => () => CaptureRegionAsync(isFixed: true),
        HotkeyAction.RepeatLast => RepeatLastCaptureAsync,
        HotkeyAction.ScrollCapture => () => CaptureScrollAsync(ScrollDirection.Vertical),
        HotkeyAction.ScrollCaptureHorizontal => () => CaptureScrollAsync(ScrollDirection.Horizontal),
        _ => () => Task.CompletedTask,
    });

    /// <summary>Chạy 1 thao tác chụp gọi từ phím tắt / menu khay (không có ai await). Trước đây dùng
    /// <c>_ = Task</c> nên exception bị nuốt mất - người dùng chỉ thấy "không hoạt động". Giờ bắt lỗi,
    /// hiện lên cửa sổ chính và ghi log (Logs\).</summary>
    private async void RunInBackground(Func<Task> capture)
    {
        try
        {
            await capture();
        }
        catch (Exception ex)
        {
            _isCapturing = false;
            RestoreLauncherAfterCapture();
            RestoreEditorAfterCancel();
            ShowLauncher();
            ShowStatus($"Chụp thất bại: {ex.Message}", InfoBarSeverity.Error);
            Log.LogError(ex, "Chụp thất bại (phím tắt / menu khay)");
        }
    }

    private async void FullScreenButton_Click(object sender, RoutedEventArgs e) => await CaptureFullScreenAsync();
    private async void WindowButton_Click(object sender, RoutedEventArgs e) => await CaptureActiveWindowAsync();
    private async void RegionButton_Click(object sender, RoutedEventArgs e) => await CaptureRegionAsync(isFixed: false);
    private async void FixedRegionButton_Click(object sender, RoutedEventArgs e) => await CaptureRegionAsync(isFixed: true);
    private async void ScrollButton_Click(object sender, RoutedEventArgs e) => await CaptureScrollAsync(ScrollDirection.Vertical);
    private async void ScrollHorizontalButton_Click(object sender, RoutedEventArgs e) => await CaptureScrollAsync(ScrollDirection.Horizontal);

    /// <summary>Chụp cuộn: chọn vùng nội dung trên ảnh màn hình đứng yên (overlay như Vùng chọn) → overlay
    /// đóng → ScrollCaptureService lăn chuột trong vùng đó, chụp + ghép tới cuối trang / Esc / giới hạn.</summary>
    private async Task CaptureScrollAsync(ScrollDirection direction)
    {
        bool horizontal = direction == ScrollDirection.Horizontal;
        if (!await BeginCaptureAsync())
        {
            return;
        }
        try
        {
            var virtualRect = _captureService.GetVirtualScreenRect();
            var frozenScreen = _captureService.CaptureRect(virtualRect);
            var overlay = new RegionOverlayWindow(frozenScreen, virtualRect, isFixed: false, null, horizontal
                ? "Chụp cuộn NGANG →: kéo chọn vùng nội dung cần cuộn sang phải (bỏ cột cố định bên trái) — thả chuột để bắt đầu. Esc để dừng."
                : "Chụp cuộn DỌC ↓: kéo chọn vùng nội dung cần cuộn xuống (bỏ thanh menu cố định) — thả chuột để bắt đầu. Esc để dừng.");
            var selection = await overlay.SelectRegionAsync();
            if (selection is null)
            {
                CancelCapture("Đã huỷ chụp cuộn.");
                return;
            }

            await Task.Delay(250); // chờ overlay đóng hẳn, cửa sổ bên dưới vẽ lại
            var scroller = new ScrollCaptureService(_captureService, _settings);
            var result = await scroller.CaptureAsync(selection.Value, direction);
            Log.LogInformation("Chụp cuộn {Direction}: vùng {W}x{H}, {Frames} khung, ảnh {Width}x{Height}, dừng: {Reason}",
                direction, selection.Value.Right - selection.Value.Left, selection.Value.Bottom - selection.Value.Top,
                result.Frames, result.Image.Width, result.Image.Height, result.Reason);
            await FinishCaptureAsync(result.Image);

            string reason = result.Reason switch
            {
                ScrollStopReason.ReachedEnd => horizontal ? "đã tới mép phải" : "đã tới cuối",
                ScrollStopReason.Cancelled => "dừng bằng Esc",
                ScrollStopReason.LimitReached => $"chạm giới hạn ({scroller.MaxSteps} lần cuộn / {scroller.MaxLength}px — đổi trong Cài đặt > Chụp cuộn)",
                _ => "không ghép tiếp được (nội dung thay đổi hoặc cuộn quá xa) - đã giữ phần ghép được",
            };
            if (_editor?.CurrentDocument is { } document)
            {
                document.StatusText = $"Chụp cuộn {(horizontal ? "ngang" : "dọc")}: {result.Frames} khung, {result.Image.Width} × {result.Image.Height} px — {reason}.";
            }
        }
        finally
        {
            _isCapturing = false;
        }
    }

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

            // Vùng chọn: kéo = chọn vùng, click = chụp cả cửa sổ đang tô viền dưới con trỏ. Liệt kê cửa sổ
            // cùng lúc chụp ảnh nền đứng yên để khung khớp với ảnh.
            var overlay = isFixed
                ? new RegionOverlayWindow(frozenScreen, virtualRect, isFixed: true, LastFixedRegionOn(virtualRect))
                : new RegionOverlayWindow(frozenScreen, virtualRect, isFixed: false, null,
                    "Kéo chuột để chọn vùng — hoặc click để chụp cả cửa sổ đang tô viền. Esc: huỷ",
                    WindowEnumerator.GetVisibleWindowRects());
            var selection = await overlay.SelectRegionAsync();
            if (selection is null)
            {
                CancelCapture("Đã huỷ chọn vùng.");
                return;
            }

            if (isFixed)
            {
                SaveLastFixedRegion(selection.Value);
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

    /// <summary>Vùng cố định đã lưu (settings.json), cắt cho vừa màn hình hiện tại - màn hình có thể đã đổi
    /// từ lần trước (rút màn hình phụ, đổi độ phân giải). Còn quá nhỏ / nằm ngoài hẳn thì bỏ, chọn vùng mới.</summary>
    private RECT? LastFixedRegionOn(RECT screen)
    {
        if (_settings.LastFixedRegion is not { } r)
        {
            return null;
        }
        var clipped = new RECT
        {
            Left = Math.Max(r.Left, screen.Left),
            Top = Math.Max(r.Top, screen.Top),
            Right = Math.Min(r.Right, screen.Right),
            Bottom = Math.Min(r.Bottom, screen.Bottom),
        };
        return clipped.Right - clipped.Left >= 10 && clipped.Bottom - clipped.Top >= 10 ? clipped : null;
    }

    private void SaveLastFixedRegion(RECT region)
    {
        _settings.LastFixedRegion = new ScreenRegion { Left = region.Left, Top = region.Top, Right = region.Right, Bottom = region.Bottom };
        try
        {
            _settingsStore.Save(_settings);
        }
        catch
        {
            // Không lưu được thì vẫn nhớ trong lần chạy này (_settings), như trước đây.
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
        Log.LogInformation("Chụp xong: {Width}x{Height}", bitmap.Width, bitmap.Height);
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
                Log.LogError(ex, "Tự động lưu thất bại ({Folder})", _settings.AutoSaveFolder);
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
                Log.LogError(ex, "Không copy được ảnh chụp vào clipboard");
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
        // Chụp bằng phím tắt / menu khay khi launcher đang thu nhỏ hoặc ẩn ở khay → chụp xong giữ nguyên,
        // không bật lên. Cửa sổ đang ẩn thì không gọi SW_MINIMIZE (sẽ làm nó hiện ra ở taskbar).
        bool visible = AppWindow.IsVisible;
        _launcherWasMinimized = !visible || NativeMethods.IsIconic(launcherHwnd);
        if (visible)
        {
            NativeMethods.ShowWindow(launcherHwnd, NativeMethods.SW_MINIMIZE);
        }
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
