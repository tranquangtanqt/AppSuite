using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ScreenCapture.Models;
using ScreenCapture.Services.Interop;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using Windows.Foundation;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace ScreenCapture.Views;

/// <summary>
/// Hút màu trên màn hình (2026-11-09): ảnh màn hình đứng yên phủ kín màn hình ảo (như Vùng chọn), kính lúp cạnh con trỏ
/// phóng to <see cref="Cells"/>×<see cref="Cells"/> pixel quanh nó + mã màu của pixel giữa. Bấm chuột = lấy màu đó, Esc / chuột
/// phải = huỷ. Màu đọc từ ảnh đứng yên nên đúng pixel người dùng nhìn thấy lúc bắt đầu.
/// </summary>
public sealed class ColorPickOverlayWindow : Window
{
    private const int Cells = 11, CellPx = 12; // kính lúp 132 px vật lý

    private readonly SKBitmap _frozenScreen;
    private readonly RECT _virtualRect;
    private readonly Image _background = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Colors.Transparent) };
    private readonly Border _loupe;
    private readonly Image _loupeImage = new() { Stretch = Stretch.Fill };
    private readonly Microsoft.UI.Xaml.Shapes.Rectangle _swatch = new() { Width = 16, Height = 16, Stroke = new SolidColorBrush(Colors.Gray), StrokeThickness = 1 };
    private readonly TextBlock _htmlText = new() { Foreground = new SolidColorBrush(Colors.White), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _rgbText = new() { Foreground = new SolidColorBrush(Colors.White), FontSize = 12, Opacity = 0.85 };
    private readonly SKBitmap _loupeBitmap = new(Cells * CellPx, Cells * CellPx);
    private readonly DispatcherTimer _keyTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private TaskCompletionSource<SKColor?>? _tcs;
    private bool _finished;
    private SKColor _current;

    public ColorPickOverlayWindow(SKBitmap frozenScreen, RECT virtualRect)
    {
        _frozenScreen = frozenScreen;
        _virtualRect = virtualRect;
        Title = "ScreenCapture - hút màu";

        var hint = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 24, 0, 0),
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(4),
            IsHitTestVisible = false,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(153, 0, 0, 0)),
            Child = new TextBlock { Text = "Hút màu: bấm chuột vào điểm cần lấy màu. Esc / chuột phải: huỷ", Foreground = new SolidColorBrush(Colors.White), FontSize = 16 },
        };

        var codes = new StackPanel { Spacing = 2, Margin = new Thickness(8, 6, 8, 6) };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        top.Children.Add(_swatch);
        top.Children.Add(_htmlText);
        codes.Children.Add(top);
        codes.Children.Add(_rgbText);
        var loupeContent = new StackPanel();
        loupeContent.Children.Add(_loupeImage);
        loupeContent.Children.Add(codes);
        _loupe = new Border
        {
            Child = loupeContent,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(230, 32, 32, 32)),
            BorderBrush = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        _canvas.Children.Add(_loupe);

        var root = new Grid();
        root.Children.Add(_background);
        root.Children.Add(_canvas);
        root.Children.Add(hint);
        Content = root;
        root.Loaded += (_, _) => FitBackground();

        _canvas.PointerMoved += (_, e) => MoveTo(e.GetCurrentPoint(_canvas).Position);
        _canvas.PointerPressed += Canvas_PointerPressed;
        root.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                Finish(null);
                e.Handled = true;
            }
        };

        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = !System.Diagnostics.Debugger.IsAttached; // như RegionOverlayWindow: không đè hộp thoại của VS
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(virtualRect.Left, virtualRect.Top, virtualRect.Width, virtualRect.Height));

        // Mở từ phím tắt thì Windows có thể không cho overlay lên foreground → không nhận KeyDown: hỏi thẳng phím Esc.
        NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE);
        _keyTimer.Tick += (_, _) =>
        {
            if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0)
            {
                Finish(null);
            }
        };
        Closed += (_, _) =>
        {
            _keyTimer.Stop();
            _loupeBitmap.Dispose();
            _tcs?.TrySetResult(null);
        };
    }

    /// <summary>Hiện overlay, trả màu đã bấm (null = huỷ).</summary>
    public async Task<SKColor?> PickAsync()
    {
        _tcs = new TaskCompletionSource<SKColor?>();
        _keyTimer.Start();
        using (var image = SKImage.FromBitmap(_frozenScreen))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            _background.Source = await LoadBitmapAsync(data.ToArray());
        }
        Activate();
        NativeMethods.SetForegroundWindow(WindowNative.GetWindowHandle(this));
        FitBackground();
        double scale = Scale;
        if (NativeMethods.GetCursorPos(out var cursor))
        {
            MoveTo(new Point((cursor.X - _virtualRect.Left) / scale, (cursor.Y - _virtualRect.Top) / scale));
        }
        return await _tcs.Task;
    }

    private double Scale => Content.XamlRoot?.RasterizationScale ?? 1.0;

    /// <summary>Ảnh chụp tính bằng pixel vật lý, bố cục XAML tính bằng DIP → co ảnh về đúng cỡ màn hình.</summary>
    private void FitBackground()
    {
        _background.Width = _virtualRect.Width / Scale;
        _background.Height = _virtualRect.Height / Scale;
    }

    private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_canvas);
        if (point.Properties.IsRightButtonPressed)
        {
            Finish(null);
            return;
        }
        MoveTo(point.Position);
        Finish(_current);
    }

    private void MoveTo(Point pos)
    {
        double scale = Scale;
        int x = Math.Clamp((int)Math.Floor(pos.X * scale), 0, _frozenScreen.Width - 1);
        int y = Math.Clamp((int)Math.Floor(pos.Y * scale), 0, _frozenScreen.Height - 1);
        _current = _frozenScreen.GetPixel(x, y).WithAlpha(255);

        DrawLoupe(x, y);
        _loupeImage.Source = _loupeBitmap.ToWriteableBitmap();
        _loupeImage.Width = _loupeImage.Height = Cells * CellPx / scale;
        _swatch.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, _current.Red, _current.Green, _current.Blue));
        _htmlText.Text = ColorTools.ToHtml(_current);
        _rgbText.Text = ColorTools.ToRgb(_current);

        // Kính lúp ở dưới-phải con trỏ; sát mép phải / dưới thì lật sang trái / lên trên.
        _loupe.Visibility = Visibility.Visible;
        _loupe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = _loupe.DesiredSize.Width, h = _loupe.DesiredSize.Height;
        double left = pos.X + 20 + w > _canvas.ActualWidth ? pos.X - 20 - w : pos.X + 20;
        double topPos = pos.Y + 20 + h > _canvas.ActualHeight ? pos.Y - 20 - h : pos.Y + 20;
        Canvas.SetLeft(_loupe, Math.Max(0, left));
        Canvas.SetTop(_loupe, Math.Max(0, topPos));
    }

    /// <summary>Vẽ <see cref="Cells"/>×<see cref="Cells"/> pixel quanh (x, y), mỗi pixel 1 ô <see cref="CellPx"/> px, ô giữa có viền.</summary>
    private void DrawLoupe(int cx, int cy)
    {
        using var canvas = new SKCanvas(_loupeBitmap);
        canvas.Clear(SKColors.Black);
        using var fill = new SKPaint();
        int half = Cells / 2;
        for (int row = 0; row < Cells; row++)
        {
            for (int col = 0; col < Cells; col++)
            {
                int x = cx - half + col, y = cy - half + row;
                fill.Color = x >= 0 && y >= 0 && x < _frozenScreen.Width && y < _frozenScreen.Height
                    ? _frozenScreen.GetPixel(x, y).WithAlpha(255)
                    : new SKColor(40, 40, 40);
                canvas.DrawRect(col * CellPx, row * CellPx, CellPx, CellPx, fill);
            }
        }
        using var grid = new SKPaint { Color = new SKColor(128, 128, 128, 70), StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        for (int i = 1; i < Cells; i++)
        {
            canvas.DrawLine(i * CellPx, 0, i * CellPx, Cells * CellPx, grid);
            canvas.DrawLine(0, i * CellPx, Cells * CellPx, i * CellPx, grid);
        }
        // Ô giữa: viền đen + trắng để thấy trên mọi màu.
        using var outer = new SKPaint { Color = SKColors.Black, StrokeWidth = 3, Style = SKPaintStyle.Stroke };
        using var inner = new SKPaint { Color = SKColors.White, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        var center = new SKRect(half * CellPx, half * CellPx, (half + 1) * CellPx, (half + 1) * CellPx);
        canvas.DrawRect(center, outer);
        canvas.DrawRect(center, inner);
    }

    private void Finish(SKColor? color)
    {
        if (_finished)
        {
            return;
        }
        _finished = true;
        _tcs?.TrySetResult(color);
        Close();
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
