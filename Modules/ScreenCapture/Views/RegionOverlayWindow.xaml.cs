using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using ScreenCapture.Services.Interop;
using SkiaSharp;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI;
using WinRT.Interop;

namespace ScreenCapture.Views;

/// <summary>
/// Freeze-then-select overlay for Region/Fixed Region capture (PLAN.md mục 2). Shows a static
/// full-virtual-screen screenshot full-screen/topmost/borderless, lets the user drag a rectangle on
/// top of it. Region mode confirms on pointer release; Fixed Region mode switches to an "adjust" state
/// with 4 corner handles and confirms on Enter (Escape cancels either mode).
/// </summary>
public sealed partial class RegionOverlayWindow : Window
{
    private readonly SKBitmap _frozenScreen;
    private readonly RECT _virtualRect;
    private readonly bool _isFixed;
    private readonly RECT? _initialFixedRegion;
    private TaskCompletionSource<RECT?>? _tcs;

    private readonly Rectangle _selectionBorder = new()
    {
        Stroke = new SolidColorBrush(Colors.DeepSkyBlue),
        StrokeThickness = 2,
        Fill = new SolidColorBrush(Colors.Transparent),
    };
    private readonly Rectangle[] _dimBands = new Rectangle[4]; // top, bottom, left, right
    private readonly Rectangle[] _handles = new Rectangle[4]; // corners: TL, TR, BL, BR

    private bool _isDragging;
    private bool _isAdjusting;
    private int _draggedHandleIndex = -1;
    private Point _dragStart;
    private Rect _selectionLogical; // logical (XAML-space) pixels, converted to device px on confirm

    /// <param name="hint">Dòng hướng dẫn thay cho mặc định "Kéo chuột để chọn vùng" (vd chụp cuộn).</param>
    public RegionOverlayWindow(SKBitmap frozenScreen, RECT virtualRect, bool isFixed, RECT? initialFixedRegion, string? hint = null)
    {
        InitializeComponent();
        if (hint is not null)
        {
            HintText.Text = hint;
        }
        _frozenScreen = frozenScreen;
        _virtualRect = virtualRect;
        _isFixed = isFixed;
        _initialFixedRegion = initialFixedRegion;

        var presenter = OverlappedPresenter.Create();
        // Luôn nằm trên cùng khi chạy thật (không bị cửa sổ khác che lúc chọn vùng). Riêng khi có
        // debugger gắn vào thì tắt: overlay full-screen topmost đè lên cả breakpoint/exception dialog
        // của Visual Studio, không Alt+Tab sang được.
        presenter.IsAlwaysOnTop = !System.Diagnostics.Debugger.IsAttached;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            virtualRect.Left, virtualRect.Top, virtualRect.Width, virtualRect.Height));

        for (int i = 0; i < 4; i++)
        {
            _dimBands[i] = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)) };
            OverlayCanvas.Children.Add(_dimBands[i]);
        }
        for (int i = 0; i < 4; i++)
        {
            _handles[i] = new Rectangle
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(Colors.White),
                Stroke = new SolidColorBrush(Colors.DeepSkyBlue),
                StrokeThickness = 1,
                Visibility = Visibility.Collapsed,
            };
            OverlayCanvas.Children.Add(_handles[i]);
        }
        OverlayCanvas.Children.Add(_selectionBorder);
        _selectionBorder.Visibility = Visibility.Collapsed;

        this.Activated += RegionOverlayWindow_Activated;
    }

    public async Task<RECT?> SelectRegionAsync()
    {
        _tcs = new TaskCompletionSource<RECT?>();
        using var image = SKImage.FromBitmap(_frozenScreen);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        BackgroundImage.Source = await LoadBitmapAsync(data.ToArray());
        this.Activate();
        return await _tcs.Task;
    }

    private async void RegionOverlayWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        this.Activated -= RegionOverlayWindow_Activated;

        // BackgroundImage.Source is a BitmapImage sized in DEVICE pixels (the raw capture), but
        // Image/Canvas layout in WinUI works in DIPs (logical pixels). Without this, on any monitor
        // with DPI scaling above 100% the image (and therefore the whole overlay) renders visually
        // larger than the real screen - explicitly size it down to the window's logical size so 1
        // logical pixel here == 1 physical screen pixel, matching what the user actually sees.
        double dpiScale = Content.XamlRoot?.RasterizationScale ?? 1.0;
        BackgroundImage.Width = _virtualRect.Width / dpiScale;
        BackgroundImage.Height = _virtualRect.Height / dpiScale;

        if (_isFixed && _initialFixedRegion is { } prevRegion)
        {
            double scale = dpiScale;
            _selectionLogical = new Rect(
                (prevRegion.Left - _virtualRect.Left) / scale,
                (prevRegion.Top - _virtualRect.Top) / scale,
                prevRegion.Width / scale,
                prevRegion.Height / scale);
            EnterAdjustState();
        }

        await Task.CompletedTask;
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

    private double Scale => Content.XamlRoot?.RasterizationScale ?? 1.0;

    private void OverlayCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(OverlayCanvas).Position;

        // Check if pressing on a resize handle first (Fixed Region adjust state).
        if (_isAdjusting)
        {
            for (int i = 0; i < _handles.Length; i++)
            {
                if (_handles[i].Visibility == Visibility.Visible && HitTestHandle(_handles[i], pos))
                {
                    _draggedHandleIndex = i;
                    OverlayCanvas.CapturePointer(e.Pointer);
                    return;
                }
            }
        }

        _isDragging = true;
        _isAdjusting = false;
        _draggedHandleIndex = -1;
        _dragStart = pos;
        _selectionLogical = new Rect(pos.X, pos.Y, 0, 0);
        _selectionBorder.Visibility = Visibility.Visible;
        HideHandles();
        UpdateSelectionVisuals();
        OverlayCanvas.CapturePointer(e.Pointer);
    }

    private void OverlayCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(OverlayCanvas).Position;

        if (_draggedHandleIndex >= 0)
        {
            ResizeFromHandle(_draggedHandleIndex, pos);
            UpdateSelectionVisuals();
            return;
        }

        if (!_isDragging)
        {
            return;
        }

        double x = Math.Min(_dragStart.X, pos.X);
        double y = Math.Min(_dragStart.Y, pos.Y);
        double w = Math.Abs(pos.X - _dragStart.X);
        double h = Math.Abs(pos.Y - _dragStart.Y);
        _selectionLogical = new Rect(x, y, w, h);
        UpdateSelectionVisuals();
    }

    private void OverlayCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        OverlayCanvas.ReleasePointerCapture(e.Pointer);

        if (_draggedHandleIndex >= 0)
        {
            _draggedHandleIndex = -1;
            return;
        }

        if (!_isDragging)
        {
            return;
        }
        _isDragging = false;

        if (_selectionLogical.Width < 4 || _selectionLogical.Height < 4)
        {
            return; // ignore accidental clicks
        }

        if (_isFixed)
        {
            EnterAdjustState();
        }
        else
        {
            Confirm();
        }
    }

    private void EnterAdjustState()
    {
        _isAdjusting = true;
        _selectionBorder.Visibility = Visibility.Visible;
        HintText.Text = "Kéo góc để chỉnh kích thước — Enter: chụp, Esc: huỷ";
        ShowHandles();
        UpdateSelectionVisuals();

        // Window-level key handling for Enter/Escape while adjusting.
        this.Content.KeyDown -= Content_KeyDown;
        this.Content.KeyDown += Content_KeyDown;
    }

    private void Content_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            Confirm();
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            Cancel();
        }
    }

    private void Confirm()
    {
        double scale = Scale;
        var rect = new RECT
        {
            Left = _virtualRect.Left + (int)Math.Round(_selectionLogical.X * scale),
            Top = _virtualRect.Top + (int)Math.Round(_selectionLogical.Y * scale),
            Right = _virtualRect.Left + (int)Math.Round((_selectionLogical.X + _selectionLogical.Width) * scale),
            Bottom = _virtualRect.Top + (int)Math.Round((_selectionLogical.Y + _selectionLogical.Height) * scale),
        };
        _tcs?.TrySetResult(rect);
        this.Close();
    }

    private void Cancel()
    {
        _tcs?.TrySetResult(null);
        this.Close();
    }

    private void HideHandles()
    {
        foreach (var handle in _handles)
        {
            handle.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowHandles()
    {
        foreach (var handle in _handles)
        {
            handle.Visibility = Visibility.Visible;
        }
    }

    private static bool HitTestHandle(Rectangle handle, Point pos)
    {
        double left = Canvas.GetLeft(handle);
        double top = Canvas.GetTop(handle);
        return pos.X >= left - 4 && pos.X <= left + handle.Width + 4 &&
               pos.Y >= top - 4 && pos.Y <= top + handle.Height + 4;
    }

    private void ResizeFromHandle(int handleIndex, Point pos)
    {
        // 0=TL, 1=TR, 2=BL, 3=BR
        double left = _selectionLogical.X, top = _selectionLogical.Y;
        double right = left + _selectionLogical.Width, bottom = top + _selectionLogical.Height;

        switch (handleIndex)
        {
            case 0: left = pos.X; top = pos.Y; break;
            case 1: right = pos.X; top = pos.Y; break;
            case 2: left = pos.X; bottom = pos.Y; break;
            case 3: right = pos.X; bottom = pos.Y; break;
        }

        if (right - left < 10 || bottom - top < 10)
        {
            return;
        }
        _selectionLogical = new Rect(left, top, right - left, bottom - top);
    }

    private void UpdateSelectionVisuals()
    {
        var r = _selectionLogical;
        Canvas.SetLeft(_selectionBorder, r.X);
        Canvas.SetTop(_selectionBorder, r.Y);
        _selectionBorder.Width = r.Width;
        _selectionBorder.Height = r.Height;

        double canvasWidth = OverlayCanvas.ActualWidth;
        double canvasHeight = OverlayCanvas.ActualHeight;

        // top band
        SetRect(_dimBands[0], 0, 0, canvasWidth, r.Y);
        // bottom band
        SetRect(_dimBands[1], 0, r.Y + r.Height, canvasWidth, Math.Max(0, canvasHeight - (r.Y + r.Height)));
        // left band (between top/bottom bands)
        SetRect(_dimBands[2], 0, r.Y, r.X, r.Height);
        // right band
        SetRect(_dimBands[3], r.X + r.Width, r.Y, Math.Max(0, canvasWidth - (r.X + r.Width)), r.Height);

        const double half = 5;
        PositionHandle(_handles[0], r.X - half, r.Y - half);
        PositionHandle(_handles[1], r.X + r.Width - half, r.Y - half);
        PositionHandle(_handles[2], r.X - half, r.Y + r.Height - half);
        PositionHandle(_handles[3], r.X + r.Width - half, r.Y + r.Height - half);
    }

    private static void SetRect(Rectangle rect, double x, double y, double w, double h)
    {
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        rect.Width = Math.Max(0, w);
        rect.Height = Math.Max(0, h);
    }

    private static void PositionHandle(Rectangle handle, double x, double y)
    {
        Canvas.SetLeft(handle, x);
        Canvas.SetTop(handle, y);
    }
}
