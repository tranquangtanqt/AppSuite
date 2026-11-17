using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ScreenCapture.Models;
using ScreenCapture.Services.Interop;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.System;
using WinRT.Interop;

namespace ScreenCapture.Views;

/// <summary>Kết quả Thước đo: các số đo đã ghim (hoặc số đo đang hiện nếu chưa ghim gì) + có mở ảnh trong Editor không.</summary>
public sealed record RulerResult(IReadOnlyList<RulerMeasurement> Measurements, bool OpenInEditor, RulerAxis Axis);

/// <summary>
/// Thước đo trên màn hình (2026-11-17): ảnh màn hình đứng yên phủ kín màn hình ảo (như Hút màu), đo bằng pixel vật lý.
/// 3 chế độ: Dò cạnh (rê chuột → vạch ngang / dọc dừng ở chỗ màu đổi, bấm = ghim), Chữ nhật (kéo → rộng × cao),
/// Đường thẳng (kéo A → B, Shift = ngang / dọc). Số đo và nhãn vẽ bằng <see cref="Ruler.Draw"/> trên SKXamlCanvas
/// trong suốt (cùng hàm vẽ ra ảnh khi Mở trong Editor). Kính lúp 11×11 pixel cạnh con trỏ (phím L bật / tắt).
/// </summary>
public sealed class RulerOverlayWindow : Window
{
    private const int LoupeCells = 11, LoupeCellPx = 10;

    private readonly SKBitmap _frozenScreen;
    private readonly RECT _virtualRect;
    private readonly Action<string> _copyText;
    private readonly Image _background = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly SKXamlCanvas _surface = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly UserControl _host;
    private readonly ToggleButton _edgesButton, _boxButton, _lineButton, _loupeButton;
    private readonly Slider _toleranceSlider;
    private readonly Button _axisButton;
    private readonly TextBlock _axisText = new();
    private RulerAxis _axis;
    private readonly TextBlock _valueText = new() { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 150 };
    private readonly TextBlock _hintText = new() { FontSize = 12, Opacity = 0.85, TextWrapping = TextWrapping.Wrap };
    private readonly List<RulerMeasurement> _pinned = [];
    private readonly DispatcherTimer _keyTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private TaskCompletionSource<RulerResult>? _tcs;
    private RulerMode _mode = RulerMode.Edges;
    private RulerMeasurement? _live;
    private (int X, int Y)? _dragStart;
    private int _cursorX = -1, _cursorY = -1;
    private bool _showLoupe = true, _finished;
    private string? _status;
    private readonly Border _toolbar;
    private bool _toolbarAtBottom, _pointerOverToolbar;
    private readonly DispatcherTimer _toolbarFlipTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };

    public RulerOverlayWindow(SKBitmap frozenScreen, RECT virtualRect, Action<string> copyText, RulerAxis axis = RulerAxis.Both)
    {
        _axis = axis;
        _frozenScreen = frozenScreen;
        _virtualRect = virtualRect;
        _copyText = copyText;
        Title = "ScreenCapture - thước đo";

        _edgesButton = ModeButton("Dò cạnh", "", "Rê chuột vào khoảng trống: vạch ngang / dọc tự dừng ở chỗ màu đổi (mép nút, viền ô). Bấm = ghim số đo. Phím 1", RulerMode.Edges);
        _boxButton = ModeButton("Chữ nhật", "", "Kéo chuột để đo rộng × cao. Phím 2", RulerMode.Box);
        _lineButton = ModeButton("Đường thẳng", "", "Kéo từ điểm A tới B: Δx, Δy, độ dài. Giữ Shift = ngang / dọc. Phím 3", RulerMode.Line);
        _toleranceSlider = new Slider
        {
            Minimum = 0, Maximum = 100, Value = Ruler.DefaultTolerance, Width = 110, VerticalAlignment = VerticalAlignment.Center,
            AllowFocusOnInteraction = false, IsTabStop = false,
        };
        AutomationProperties.SetName(_toleranceSlider, "Độ nhạy");
        ToolTipService.SetToolTip(_toleranceSlider, "Độ lệch màu tối đa vẫn coi là cùng màu khi Dò cạnh (0 = đúng màu). Nền có bóng mờ / gradient thì tăng lên.");
        _toleranceSlider.ValueChanged += (_, _) => UpdateLive();
        // Dò cạnh hiện chiều nào: bấm đổi vòng Ngang + dọc → Chỉ ngang → Chỉ dọc (phím H).
        var axisContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        axisContent.Children.Add(Icon(""));
        axisContent.Children.Add(_axisText);
        _axisButton = new Button { Content = axisContent, AllowFocusOnInteraction = false, IsTabStop = false };
        AutomationProperties.SetName(_axisButton, "Chiều đo");
        ToolTipService.SetToolTip(_axisButton, "Dò cạnh đo chiều nào: Ngang + dọc → Chỉ ngang (đo rộng) → Chỉ dọc (đo cao). Phím H");
        _axisButton.Click += (_, _) => SetAxis(Ruler.NextAxis(_axis));
        _axisText.Text = Ruler.AxisName(_axis);
        _loupeButton = new ToggleButton { IsChecked = true, Content = Icon(""), AllowFocusOnInteraction = false, IsTabStop = false };
        AutomationProperties.SetName(_loupeButton, "Kính lúp");
        ToolTipService.SetToolTip(_loupeButton, "Kính lúp cạnh con trỏ (phím L)");
        _loupeButton.Click += (_, _) => SetLoupe(_loupeButton.IsChecked == true);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(_edgesButton);
        row.Children.Add(_boxButton);
        row.Children.Add(_lineButton);
        row.Children.Add(new Border { Width = 1, Margin = new Thickness(4, 4, 4, 4), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(80, 255, 255, 255)) });
        row.Children.Add(new TextBlock { Text = "Độ nhạy", VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(_toleranceSlider);
        row.Children.Add(_axisButton);
        row.Children.Add(_loupeButton);
        row.Children.Add(ActionButton("Copy", "", "Copy số đo (Ctrl+C): các số đo đã ghim, chưa ghim thì số đo đang hiện", Copy));
        row.Children.Add(ActionButton("Mở trong Editor", "", "Mở ảnh màn hình kèm các vạch đo trong Editor (Enter)", () => Finish(openInEditor: true)));
        row.Children.Add(ActionButton("Xoá hết", "", "Xoá mọi số đo đã ghim (Delete). Bỏ số đo vừa ghim: Ctrl+Z / Backspace / chuột phải", ClearPinned));
        row.Children.Add(ActionButton(null, "", "Đóng thước đo (Esc)", () => Finish(openInEditor: false), "Đóng"));

        var info = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(2, 6, 2, 0) };
        info.Children.Add(_valueText);
        info.Children.Add(_hintText);
        var toolbarContent = new StackPanel();
        toolbarContent.Children.Add(row);
        toolbarContent.Children.Add(info);
        _toolbar = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left, // vị trí đặt bằng Margin trong PlaceToolbar (giữa màn hình có con trỏ)
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(16, 16, 0, 0),
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(235, 32, 32, 32)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(90, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            RequestedTheme = ElementTheme.Dark,
            Child = toolbarContent,
        };

        var root = new Grid();
        root.Children.Add(_background);
        root.Children.Add(_surface);
        root.Children.Add(_toolbar);
        _toolbar.SizeChanged += (_, _) => PlaceToolbar();
        _toolbar.PointerEntered += (_, _) =>
        {
            _pointerOverToolbar = true; // đi tới bấm nút: thanh đứng yên
            _toolbarFlipTimer.Stop();
        };
        _toolbar.PointerExited += (_, _) => _pointerOverToolbar = false;
        _toolbarFlipTimer.Tick += ToolbarFlipTimer_Tick;
        _host = new UserControl { Content = root, IsTabStop = true, UseSystemFocusVisuals = false };
        Content = _host;
        root.Loaded += (_, _) => FitToScreen();

        _surface.PaintSurface += Surface_PaintSurface;
        _surface.PointerMoved += Surface_PointerMoved;
        _surface.PointerPressed += Surface_PointerPressed;
        _surface.PointerReleased += Surface_PointerReleased;
        _host.KeyDown += Host_KeyDown;
        Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
            {
                _host.Focus(FocusState.Programmatic); // lên foreground muộn (mở từ phím tắt) vẫn nhận phím ngay
            }
        };

        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = !System.Diagnostics.Debugger.IsAttached; // như Hút màu: không đè hộp thoại của VS
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(virtualRect.Left, virtualRect.Top, virtualRect.Width, virtualRect.Height));

        // Mở từ phím tắt thì Windows có thể không cho lớp phủ lên foreground → không nhận KeyDown: hỏi thẳng phím Esc.
        NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE);
        _keyTimer.Tick += (_, _) =>
        {
            if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0)
            {
                Finish(openInEditor: false);
            }
        };
        Closed += (_, _) =>
        {
            _keyTimer.Stop();
            _toolbarFlipTimer.Stop();
            _tcs?.TrySetResult(new RulerResult(Result(), false, _axis));
        };
        SetMode(RulerMode.Edges);
    }

    /// <summary>Hiện lớp phủ, chờ tới khi đóng (Esc / Đóng) hoặc Mở trong Editor (Enter).</summary>
    public async Task<RulerResult> MeasureAsync()
    {
        _tcs = new TaskCompletionSource<RulerResult>();
        _keyTimer.Start();
        using (var image = SKImage.FromBitmap(_frozenScreen))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            _background.Source = await LoadBitmapAsync(data.ToArray());
        }
        Activate();
        WindowChrome.AlignClientTo(this, _virtualRect);
        NativeMethods.SetForegroundWindow(WindowNative.GetWindowHandle(this));
        FitToScreen();
        _host.Focus(FocusState.Programmatic);
        if (NativeMethods.GetCursorPos(out var cursor))
        {
            MoveCursorTo(cursor.X - _virtualRect.Left, cursor.Y - _virtualRect.Top, shift: false);
        }
        return await _tcs.Task;
    }
    private double Scale => Content.XamlRoot?.RasterizationScale ?? 1.0;

    /// <summary>Ảnh chụp và lớp vẽ tính bằng pixel vật lý, bố cục XAML tính bằng DIP → co về đúng cỡ màn hình.</summary>
    private void FitToScreen()
    {
        _background.Width = _surface.Width = _virtualRect.Width / Scale;
        _background.Height = _surface.Height = _virtualRect.Height / Scale;
    }

    // ---- Thanh công cụ ----

    private static FontIcon Icon(string glyph) => new() { Glyph = glyph, FontSize = 14 };

    private ToggleButton ModeButton(string text, string glyph, string tooltip, RulerMode mode)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(Icon(glyph));
        content.Children.Add(new TextBlock { Text = text });
        var button = new ToggleButton { Content = content, AllowFocusOnInteraction = false, IsTabStop = false };
        AutomationProperties.SetName(button, text);
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += (_, _) => SetMode(mode);
        return button;
    }

    private static Button ActionButton(string? text, string glyph, string tooltip, Action click, string? name = null)
    {
        object content = Icon(glyph);
        if (text is not null)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            panel.Children.Add(Icon(glyph));
            panel.Children.Add(new TextBlock { Text = text });
            content = panel;
        }
        var button = new Button { Content = content, AllowFocusOnInteraction = false, IsTabStop = false };
        AutomationProperties.SetName(button, name ?? text);
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += (_, _) => click();
        return button;
    }

    private void SetMode(RulerMode mode)
    {
        _mode = mode;
        _dragStart = null;
        _edgesButton.IsChecked = mode == RulerMode.Edges;
        _boxButton.IsChecked = mode == RulerMode.Box;
        _lineButton.IsChecked = mode == RulerMode.Line;
        _toleranceSlider.IsEnabled = mode == RulerMode.Edges;
        _axisButton.IsEnabled = mode == RulerMode.Edges;
        _status = null;
        UpdateLive();
    }

    private void SetAxis(RulerAxis axis)
    {
        _axis = axis;
        _axisText.Text = Ruler.AxisName(axis);
        UpdateLive();
    }

    private void SetLoupe(bool show)
    {
        _showLoupe = show;
        _loupeButton.IsChecked = show;
        _surface.Invalidate();
    }

    private string Hint => _mode switch
    {
        RulerMode.Edges => "Rê chuột vào khoảng trống giữa 2 thành phần · bấm = ghim · H = đổi chiều",
        RulerMode.Box => "Kéo chuột để đo rộng × cao",
        _ => "Kéo từ A tới B · Shift = ngang / dọc",
    } + " · ←↑→↓ dịch 1 px · Enter mở trong Editor · Esc thoát";

    // ---- Chuột / phím ----

    private void Surface_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(_surface).Position;
        SetCursor(pos, e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift));
    }

    private void Surface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_surface);
        _host.Focus(FocusState.Programmatic);
        if (point.Properties.IsRightButtonPressed)
        {
            if (_dragStart is not null)
            {
                _dragStart = null; // đang kéo thì chuột phải = bỏ lần kéo này
                UpdateLive();
            }
            else if (_pinned.Count > 0)
            {
                RemoveLastPinned();
            }
            else
            {
                Finish(openInEditor: false);
            }
            return;
        }
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }
        SetCursor(point.Position, shift: false);
        if (_mode == RulerMode.Edges)
        {
            if (_live is not null)
            {
                Pin(_live);
            }
            return;
        }
        _surface.CapturePointer(e.Pointer);
        _dragStart = (_cursorX, _cursorY);
        UpdateLive();
    }

    private void Surface_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _surface.ReleasePointerCapture(e.Pointer);
        if (_dragStart is null)
        {
            return;
        }
        var measurement = _live;
        _dragStart = null;
        // Bấm rồi thả tại chỗ (chữ nhật 1×1 / đoạn dài 0) = không đo gì.
        if (measurement is not null && (measurement.Mode == RulerMode.Box ? measurement.Width > 1 || measurement.Height > 1 : measurement.Length > 0))
        {
            Pin(measurement);
        }
        else
        {
            UpdateLive();
        }
    }

    private void Host_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = Down(VirtualKey.Control), shift = Down(VirtualKey.Shift);
        int step = shift ? 10 : 1;
        switch (e.Key)
        {
            case VirtualKey.Escape: Finish(openInEditor: false); break;
            case VirtualKey.Enter: Finish(openInEditor: true); break;
            case VirtualKey.Number1 or VirtualKey.NumberPad1: SetMode(RulerMode.Edges); break;
            case VirtualKey.Number2 or VirtualKey.NumberPad2: SetMode(RulerMode.Box); break;
            case VirtualKey.Number3 or VirtualKey.NumberPad3: SetMode(RulerMode.Line); break;
            case VirtualKey.L: SetLoupe(!_showLoupe); break;
            case VirtualKey.H when _mode == RulerMode.Edges: SetAxis(Ruler.NextAxis(_axis)); break;
            case VirtualKey.C when ctrl: Copy(); break;
            case VirtualKey.Z when ctrl: RemoveLastPinned(); break;
            case VirtualKey.Back: RemoveLastPinned(); break;
            case VirtualKey.Delete: ClearPinned(); break;
            case VirtualKey.Left: MoveCursorBy(-step, 0); break;
            case VirtualKey.Right: MoveCursorBy(step, 0); break;
            case VirtualKey.Up: MoveCursorBy(0, -step); break;
            case VirtualKey.Down: MoveCursorBy(0, step); break;
            default: return;
        }
        e.Handled = true;
    }

    private static bool Down(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    /// <summary>Phím mũi tên: dời con trỏ thật 1 px (Shift: 10 px) để đặt chính xác từng pixel.</summary>
    private void MoveCursorBy(int dx, int dy)
    {
        if (_cursorX < 0)
        {
            return;
        }
        int x = Math.Clamp(_cursorX + dx, 0, _frozenScreen.Width - 1), y = Math.Clamp(_cursorY + dy, 0, _frozenScreen.Height - 1);
        NativeMethods.SetCursorPos(_virtualRect.Left + x, _virtualRect.Top + y);
        MoveCursorTo(x, y, Down(VirtualKey.Shift) && _mode == RulerMode.Line);
    }

    /// <summary>
    /// Vị trí đo = con trỏ thật (GetCursorPos, pixel vật lý) - lớp phủ đã khớp đúng màn hình ảo (<see cref="WindowChrome.AlignClientTo"/>).
    /// Không dùng toạ độ DIP của sự kiện chuột: kiểu float (pixel 221 ở 150% thành 220,99998) và sự kiện đến muộn sau khi phím
    /// mũi tên đã dời con trỏ thì kéo số đo về chỗ cũ.
    /// </summary>
    private void SetCursor(Point dip, bool shift)
    {
        if (NativeMethods.GetCursorPos(out var cursor))
        {
            MoveCursorTo(cursor.X - _virtualRect.Left, cursor.Y - _virtualRect.Top, shift);
            return;
        }
        double scale = Scale;
        MoveCursorTo((int)Math.Floor(dip.X * scale + 0.01), (int)Math.Floor(dip.Y * scale + 0.01), shift);
    }

    private bool _snap;

    private void MoveCursorTo(int x, int y, bool shift)
    {
        _cursorX = Math.Clamp(x, 0, _frozenScreen.Width - 1);
        _cursorY = Math.Clamp(y, 0, _frozenScreen.Height - 1);
        _snap = shift;
        UpdateLive();
    }

    // ---- Số đo ----

    /// <summary>Tính lại số đo đang hiện theo vị trí con trỏ / chế độ / độ nhạy, rồi vẽ lại.</summary>
    private void UpdateLive()
    {
        if (_cursorX < 0)
        {
            _live = null;
        }
        else if (_dragStart is { } start)
        {
            double scale = ScaleAt(start.X, start.Y);
            if (_mode == RulerMode.Box)
            {
                _live = Ruler.Box(start.X, start.Y, _cursorX, _cursorY, scale);
            }
            else
            {
                var (bx, by) = _snap ? Ruler.SnapLine(start.X, start.Y, _cursorX, _cursorY) : (_cursorX, _cursorY);
                _live = new RulerMeasurement(RulerMode.Line, start.X, start.Y, bx, by, Scale: scale);
            }
        }
        else
        {
            _live = _mode == RulerMode.Edges
                ? Ruler.FindEdges(_frozenScreen, _cursorX, _cursorY, (int)_toleranceSlider.Value, ScaleAt(_cursorX, _cursorY)) with { Axis = _axis }
                : null;
        }

        PlaceToolbar();
        _valueText.Text = _live is not null ? _live.Label : $"x {_cursorX} · y {_cursorY}";
        _hintText.Text = _status ?? (_pinned.Count > 0 ? $"Đã ghim {_pinned.Count} · " : "") + Hint;
        _surface.Invalidate();
    }

    /// <summary>
    /// Thanh công cụ nằm giữa cạnh trên (hoặc dưới) của màn hình đang có con trỏ (nhiều màn hình: không vắt ngang 2 màn).
    /// Con trỏ sát thanh (trong <see cref="ToolbarNearDip"/>) không né ngay - người dùng có thể đang đi tới bấm nút: chỉ dời sang
    /// mép kia khi con trỏ đứng yên ở đó <see cref="_toolbarFlipTimer"/> (0,8 giây) mà không vào thanh, hoặc khi đang kéo đo tới gần.
    /// </summary>
    private void PlaceToolbar()
    {
        if (_cursorX < 0 || _toolbar.ActualWidth <= 0)
        {
            return;
        }
        if (_pointerOverToolbar || !CursorNearToolbar())
        {
            _toolbarFlipTimer.Stop();
        }
        else if (_dragStart is not null)
        {
            _toolbarFlipTimer.Stop();
            _toolbarAtBottom = !_toolbarAtBottom; // đang kéo đo: né ngay
        }
        else
        {
            _toolbarFlipTimer.Stop(); // con trỏ còn di chuyển (có thể đang đi tới bấm nút) → đếm lại; đứng yên 0,8 giây mới né
            _toolbarFlipTimer.Start();
        }
        var (monitorLeft, monitorTop, monitorWidth, monitorHeight) = CursorMonitorDip();
        double left = monitorLeft + Math.Max(0, (monitorWidth - _toolbar.ActualWidth) / 2);
        double top = _toolbarAtBottom ? monitorTop + monitorHeight - _toolbar.ActualHeight - 16 : monitorTop + 16;
        var margin = new Thickness(left, top, 0, 0);
        if (_toolbar.Margin != margin)
        {
            _toolbar.Margin = margin;
        }
    }

    /// <summary>Khoảng (DIP) quanh thanh công cụ tính là "sát thanh": phía trong màn hình rộng hơn vì kính lúp / nhãn ở cạnh con trỏ.</summary>
    private const double ToolbarNearDip = 40, ToolbarNearInwardDip = 180;

    private bool CursorNearToolbar()
    {
        double scale = Scale, x = _cursorX / scale, y = _cursorY / scale;
        double left = _toolbar.Margin.Left, top = _toolbar.Margin.Top;
        double right = left + _toolbar.ActualWidth, bottom = top + _toolbar.ActualHeight;
        return x >= left - ToolbarNearDip && x <= right + ToolbarNearDip
            && (_toolbarAtBottom
                ? y >= top - ToolbarNearInwardDip && y <= bottom + ToolbarNearDip
                : y >= top - ToolbarNearDip && y <= bottom + ToolbarNearInwardDip);
    }

    /// <summary>Màn hình đang có con trỏ, toạ độ DIP trong lớp phủ.</summary>
    private (double Left, double Top, double Width, double Height) CursorMonitorDip()
    {
        double scale = Scale;
        var area = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(_virtualRect.Left + _cursorX, _virtualRect.Top + _cursorY),
            DisplayAreaFallback.Nearest).OuterBounds;
        return ((area.X - _virtualRect.Left) / scale, (area.Y - _virtualRect.Top) / scale, area.Width / scale, area.Height / scale);
    }

    private void ToolbarFlipTimer_Tick(object? sender, object e)
    {
        _toolbarFlipTimer.Stop();
        if (!_pointerOverToolbar && CursorNearToolbar())
        {
            _toolbarAtBottom = !_toolbarAtBottom;
            PlaceToolbar();
        }
    }

    /// <summary>Tỉ lệ DPI của màn hình chứa điểm (x, y) - để ghi kèm pixel logic đúng màn đó (nhiều màn hình khác DPI).</summary>
    private double ScaleAt(int x, int y)
    {
        var monitor = NativeMethods.MonitorFromPoint(new POINT { X = _virtualRect.Left + x, Y = _virtualRect.Top + y }, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return NativeMethods.GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 ? dpiX / 96.0 : Scale;
    }

    private void Pin(RulerMeasurement measurement)
    {
        _pinned.Add(measurement);
        _status = null;
        UpdateLive();
    }

    private void RemoveLastPinned()
    {
        if (_pinned.Count > 0)
        {
            _pinned.RemoveAt(_pinned.Count - 1);
        }
        _status = null;
        UpdateLive();
    }

    private void ClearPinned()
    {
        _pinned.Clear();
        _status = null;
        UpdateLive();
    }

    private IReadOnlyList<RulerMeasurement> Result() =>
        _pinned.Count > 0 ? [.. _pinned] : _live is not null ? [_live] : [];

    private void Copy()
    {
        var measurements = Result();
        if (measurements.Count == 0)
        {
            return;
        }
        string text = string.Join(Environment.NewLine, measurements.Select(m => m.CopyText));
        _copyText(text);
        _status = measurements.Count == 1 ? $"Đã copy: {measurements[0].CopyText}" : $"Đã copy {measurements.Count} số đo";
        UpdateLive();
    }

    private void Finish(bool openInEditor)
    {
        if (_finished)
        {
            return;
        }
        _finished = true;
        _tcs?.TrySetResult(new RulerResult(Result(), openInEditor, _axis));
        Close();
    }

    // ---- Vẽ ----

    private void Surface_PaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        float ui = (float)Scale;
        if (_cursorX < 0)
        {
            return;
        }
        // Chữ nhật / Đường thẳng chưa kéo: đường gióng ngang + dọc qua con trỏ để căn thẳng hàng.
        if (_mode != RulerMode.Edges && _dragStart is null)
        {
            using var guide = new SKPaint { Color = Ruler.LineColor.WithAlpha(150), StrokeWidth = 1, Style = SKPaintStyle.Stroke };
            canvas.DrawLine(0, _cursorY + 0.5f, e.Info.Width, _cursorY + 0.5f, guide);
            canvas.DrawLine(_cursorX + 0.5f, 0, _cursorX + 0.5f, e.Info.Height, guide);
        }
        Ruler.Draw(canvas, _live is null ? _pinned : [.. _pinned, _live], ui);
        if (_showLoupe)
        {
            DrawLoupe(canvas, ui, e.Info.Width, e.Info.Height);
        }
    }

    /// <summary>Kính lúp phía trên-phải con trỏ (nhãn số đo ở dưới-phải), sát mép thì lật; kèm toạ độ pixel.</summary>
    private void DrawLoupe(SKCanvas canvas, float ui, int width, int height)
    {
        float cell = MathF.Round(LoupeCellPx * ui), size = cell * LoupeCells, gap = 24 * ui;
        float textHeight = 18 * ui;
        float left = _cursorX + gap + size > width ? _cursorX - gap - size : _cursorX + gap;
        float top = _cursorY - gap - size - textHeight < 0 ? _cursorY + gap + 40 * ui : _cursorY - gap - size - textHeight;
        top = Math.Min(top, height - size - textHeight);
        var dest = new SKRect(left, top, left + size, top + size);

        int half = LoupeCells / 2;
        var src = new SKRect(_cursorX - half, _cursorY - half, _cursorX + half + 1, _cursorY + half + 1);
        using var back = new SKPaint { Color = new SKColor(40, 40, 40) };
        canvas.DrawRect(new SKRect(dest.Left, dest.Top, dest.Right, dest.Bottom + textHeight), back);
        using (var pixels = new SKPaint { FilterQuality = SKFilterQuality.None })
        {
            canvas.Save();
            canvas.ClipRect(dest);
            canvas.DrawBitmap(_frozenScreen, src, dest, pixels);
            canvas.Restore();
        }
        using var grid = new SKPaint { Color = new SKColor(128, 128, 128, 70), StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        for (int i = 1; i < LoupeCells; i++)
        {
            canvas.DrawLine(dest.Left + i * cell, dest.Top, dest.Left + i * cell, dest.Bottom, grid);
            canvas.DrawLine(dest.Left, dest.Top + i * cell, dest.Right, dest.Top + i * cell, grid);
        }
        var center = new SKRect(dest.Left + half * cell, dest.Top + half * cell, dest.Left + (half + 1) * cell, dest.Top + (half + 1) * cell);
        using var outer = new SKPaint { Color = SKColors.Black, StrokeWidth = 3, Style = SKPaintStyle.Stroke };
        using var inner = new SKPaint { Color = SKColors.White, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        canvas.DrawRect(center, outer);
        canvas.DrawRect(center, inner);
        using var border = new SKPaint { Color = SKColors.White, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        canvas.DrawRect(new SKRect(dest.Left, dest.Top, dest.Right, dest.Bottom + textHeight), border);
        using var text = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 11 * ui, Typeface = SKTypeface.Default };
        canvas.DrawText($"x {_cursorX} · y {_cursorY}", dest.Left + 6 * ui, dest.Bottom + textHeight - 5 * ui, text);
    }

    private static async Task<BitmapImage> LoadBitmapAsync(byte[] pngBytes)
    {
        var image = new BitmapImage();
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(pngBytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        await image.SetSourceAsync(stream);
        return image;
    }
}
