using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using ScreenCapture.Models;
using SkiaSharp;
using SkiaSharp.Views.Windows;

namespace ScreenCapture.Views;

/// <summary>
/// Thước đo trong Editor (2026-11-17, hướng B của Thước đo): đo pixel trên ảnh đang mở - cùng cách đo với Thước đo trên màn
/// hình (<see cref="Ruler"/>) nhưng theo pixel ảnh, có zoom. Rê chuột = dò khoảng trống tới chỗ màu đổi (bấm = ghim), kéo = chữ
/// nhật, Ctrl + kéo = đường thẳng (thêm Shift: ngang / dọc). Số đo chỉ vẽ đè lên (không vào ảnh) cho tới khi "Chèn vào ảnh".
/// Thước kẻ ở mép trên / trái khung ảnh chạy theo zoom + cuộn, đánh dấu vị trí con trỏ và khoảng đang đo.
/// </summary>
public sealed partial class EditorWindow
{
    private readonly List<RulerMeasurement> _measurements = [];
    /// <summary>Số đo theo chuột (chưa ghim).</summary>
    private RulerMeasurement? _measureLive;
    /// <summary>Pixel ảnh lúc bấm chuột (đang kéo đo), null = không kéo.</summary>
    private SKPointI? _measureDragStart;
    private SKPoint _measureDragStartPos;
    /// <summary>Pixel ảnh dưới con trỏ (vạch hồng trên thước kẻ), null = chuột ngoài ảnh.</summary>
    private SKPointI? _measureCursor;
    /// <summary>Ảnh mà các số đo thuộc về: cắt / xoay / đổi cỡ ảnh thì toạ độ cũ không còn đúng → bỏ số đo.</summary>
    private SKBitmap? _measureBitmap;
    /// <summary>Thông báo việc vừa làm (Đã copy / Đã chèn) giữ trên thanh trạng thái cho tới lần bấm sau.</summary>
    private string? _measureNotice;
    private RulerAxis _measureAxis = RulerAxis.Both;

    /// <summary>Chiều Dò cạnh đã lưu (launcher đọc từ cài đặt) - đọc mỗi lần bật Thước đo để khớp với Thước đo trên màn hình.</summary>
    public Func<RulerAxis>? RulerAxisSource { get; set; }

    /// <summary>Người dùng đổi chiều Dò cạnh (nút Ngang + dọc / phím H) → launcher lưu cài đặt.</summary>
    public event EventHandler<RulerAxis>? RulerAxisChanged;

    private bool IsMeasuring => _viewModel.SelectedTool == CaptureTool.Measure && !_isCropping;

    private void MeasureToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Measure, MeasureToolButton);
    private void MeasureCopyButton_Click(object sender, RoutedEventArgs e) => CopyMeasurements();
    private void MeasureInsertButton_Click(object sender, RoutedEventArgs e) => InsertMeasurements();
    private void MeasureClearButton_Click(object sender, RoutedEventArgs e) => ClearMeasurements();
    private void MeasureAxisButton_Click(object sender, RoutedEventArgs e) => SetMeasureAxis(Ruler.NextAxis(_measureAxis));

    private void SetMeasureAxis(RulerAxis axis)
    {
        _measureAxis = axis;
        MeasureAxisText.Text = Ruler.AxisName(axis);
        RulerAxisChanged?.Invoke(this, axis);
        if (_measureLive is { Mode: RulerMode.Edges } live)
        {
            _measureLive = live with { Axis = axis };
        }
        ShowMeasureStatus();
        Canvas.Invalidate();
    }

    /// <summary>Gọi khi đổi công cụ / bật Cắt: hiện / ẩn thước kẻ; rời Thước đo thì bỏ các số đo (chỉ là số đo tạm).</summary>
    private void UpdateMeasureTool()
    {
        bool on = IsMeasuring;
        if (on && RulerAxisSource is not null)
        {
            _measureAxis = RulerAxisSource();
            MeasureAxisText.Text = Ruler.AxisName(_measureAxis);
        }
        HorizontalRuler.Visibility = VerticalRuler.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on)
        {
            ResetMeasurements();
        }
        else
        {
            StatusText.Text = "Thước đo: rê chuột = dò khoảng trống, bấm = ghim · kéo = chữ nhật · Ctrl + kéo = đường thẳng";
        }
        UpdateMeasureButtons();
        InvalidateRulers();
    }

    /// <summary>Bỏ mọi số đo + thao tác kéo dở (đổi tab, rời công cụ).</summary>
    private void ResetMeasurements()
    {
        _measurements.Clear();
        _measureLive = null;
        _measureDragStart = null;
        _measureCursor = null;
        _measureBitmap = null;
        _measureNotice = null;
        UpdateMeasureButtons();
    }

    private void UpdateMeasureButtons()
    {
        bool any = _measurements.Count > 0;
        MeasureCopyButton.IsEnabled = MeasureInsertButton.IsEnabled = MeasureClearButton.IsEnabled = any;
    }

    private SKRect ImageRect => SKRect.Create(_viewModel.Bitmap.Width, _viewModel.Bitmap.Height);

    /// <summary>Số đo cũ thuộc ảnh trước khi cắt / xoay / đổi cỡ → bỏ.</summary>
    private void DropMeasurementsIfImageChanged()
    {
        if (_measureBitmap is not null && !ReferenceEquals(_measureBitmap, _viewModel.Bitmap))
        {
            ResetMeasurements();
        }
        _measureBitmap = _viewModel.Bitmap;
    }

    private bool InsideImage(SKPoint pos) =>
        pos.X >= 0 && pos.Y >= 0 && pos.X < _viewModel.Bitmap.Width && pos.Y < _viewModel.Bitmap.Height;

    private SKPointI PixelAt(SKPoint pos) => new(
        Math.Clamp((int)MathF.Floor(pos.X), 0, _viewModel.Bitmap.Width - 1),
        Math.Clamp((int)MathF.Floor(pos.Y), 0, _viewModel.Bitmap.Height - 1));

    private RulerMeasurement? EdgesAt(SKPointI p) =>
        _viewModel.Bitmap.BytesPerPixel == 4 ? Ruler.FindEdges(_viewModel.Bitmap, p.X, p.Y, Ruler.DefaultTolerance) with { Axis = _measureAxis } : null;

    private void MeasurePressed(SKPoint pos, PointerRoutedEventArgs e)
    {
        DropMeasurementsIfImageChanged();
        // Tiêu điểm còn ở nút Thước đo (vừa bấm) thì Enter / Space bấm lại nút đó → đưa về khung ảnh cho phím của Thước đo.
        CanvasScroller.Focus(FocusState.Pointer);
        _measureNotice = null;
        var point = e.GetCurrentPoint(Canvas);
        if (point.Properties.IsRightButtonPressed)
        {
            // Chuột phải: đang kéo thì huỷ, không thì bỏ số đo vừa ghim.
            if (_measureDragStart is not null)
            {
                _measureDragStart = null;
                _measureLive = InsideImage(pos) ? EdgesAt(PixelAt(pos)) : null;
            }
            else
            {
                RemoveLastMeasurement();
            }
            Canvas.Invalidate();
            return;
        }
        if (!point.Properties.IsLeftButtonPressed || !InsideImage(pos))
        {
            return;
        }
        var pixel = PixelAt(pos);
        _measureDragStart = pixel;
        _measureDragStartPos = pos;
        _measureCursor = pixel;
        _measureLive = EdgesAt(pixel); // thả chuột không kéo = ghim số đo dò cạnh ở chỗ bấm
        Canvas.CapturePointer(e.Pointer);
        ShowMeasureStatus();
        Canvas.Invalidate();
    }

    private void MeasureMoved(SKPoint pos, PointerRoutedEventArgs e)
    {
        DropMeasurementsIfImageChanged();
        var pixel = PixelAt(pos);
        _measureCursor = InsideImage(pos) || _measureDragStart is not null ? pixel : null;
        if (_measureDragStart is { } start)
        {
            // Rung tay vài pixel màn hình khi bấm vẫn tính là bấm (ghim dò cạnh), không phải kéo.
            bool dragged = SKPoint.Distance(pos, _measureDragStartPos) * _zoom >= 4 * CurrentScale;
            if (dragged)
            {
                bool ctrl = e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control);
                bool shift = e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift);
                if (ctrl)
                {
                    var (x, y) = shift ? Ruler.SnapLine(start.X, start.Y, pixel.X, pixel.Y) : (pixel.X, pixel.Y);
                    _measureLive = new RulerMeasurement(RulerMode.Line, start.X, start.Y, x, y);
                }
                else
                {
                    _measureLive = Ruler.Box(start.X, start.Y, pixel.X, pixel.Y);
                }
            }
        }
        else
        {
            _measureLive = _measureCursor is { } p ? EdgesAt(p) : null;
        }
        ShowMeasureStatus();
        Canvas.Invalidate();
    }

    private void MeasureReleased()
    {
        if (_measureDragStart is null)
        {
            return;
        }
        _measureDragStart = null;
        if (_measureLive is { } m)
        {
            _measurements.Add(m);
            _measureLive = null; // số đo vừa ghim đã vẽ; rê chuột tiếp sẽ có số đo theo chuột mới
        }
        UpdateMeasureButtons();
        ShowMeasureStatus();
        Canvas.Invalidate();
    }

    private void Canvas_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (IsMeasuring && _measureDragStart is null)
        {
            _measureLive = null;
            _measureCursor = null;
            Canvas.Invalidate();
        }
    }

    private void ShowMeasureStatus()
    {
        string cursor = _measureCursor is { } c ? $"Con trỏ ({c.X}, {c.Y})" : "";
        var current = _measureLive ?? _measurements.LastOrDefault();
        string value = current is null ? "" : $"{ModeName(current.Mode)}: {current.Label}";
        string pinned = _measurements.Count > 0 ? $"{_measurements.Count} số đo đã ghim" : "";
        StatusText.Text = string.Join(" · ", new[] { value, cursor, pinned, _measureNotice ?? "" }.Where(s => s.Length > 0));
    }

    private static string ModeName(RulerMode mode) => mode switch
    {
        RulerMode.Box => "Chữ nhật",
        RulerMode.Line => "Đường thẳng",
        _ => "Dò cạnh",
    };

    private void RemoveLastMeasurement()
    {
        if (_measurements.Count > 0)
        {
            _measurements.RemoveAt(_measurements.Count - 1);
        }
        UpdateMeasureButtons();
        ShowMeasureStatus();
        Canvas.Invalidate();
    }

    private void ClearMeasurements()
    {
        _measurements.Clear();
        UpdateMeasureButtons();
        ShowMeasureStatus();
        Canvas.Invalidate();
    }

    private void CopyMeasurements()
    {
        if (_measurements.Count == 0)
        {
            return;
        }
        _clipboardService.CopyText(string.Join(Environment.NewLine, _measurements.Select(m => m.CopyText)));
        _measureNotice = _measurements.Count == 1 ? "Đã copy số đo" : $"Đã copy {_measurements.Count} số đo";
        ShowMeasureStatus();
    }

    /// <summary>Các số đo đang ghim thành hình vẽ thường (1 bước Hoàn tác), rồi bỏ số đo tạm.</summary>
    private void InsertMeasurements()
    {
        DropMeasurementsIfImageChanged();
        int count = _measurements.Count;
        if (count == 0)
        {
            return;
        }
        var shapes = _measurements.SelectMany(m => Ruler.ToAnnotations(m, ImageRect)).ToList();
        _viewModel.AddAnnotations(shapes, count == 1 ? "Chèn số đo vào ảnh" : $"Chèn {count} số đo vào ảnh");
        _measurements.Clear();
        UpdateMeasureButtons();
        _measureNotice = count == 1 ? "Đã chèn số đo vào ảnh (Ctrl+Z: bỏ)" : $"Đã chèn {count} số đo vào ảnh (Ctrl+Z: bỏ)";
        ShowMeasureStatus();
        Canvas.Invalidate();
    }

    /// <summary>Phím khi đang dùng Thước đo. True = đã xử lý.</summary>
    private bool HandleMeasureKey(Windows.System.VirtualKey key, bool ctrl)
    {
        if (IsMeasuring && key == Windows.System.VirtualKey.H && !ctrl)
        {
            SetMeasureAxis(Ruler.NextAxis(_measureAxis));
            return true;
        }
        if (!IsMeasuring || _measurements.Count == 0 && _measureDragStart is null)
        {
            return false; // chưa ghim gì: Ctrl+C copy ảnh, Esc... như bình thường
        }
        switch (key)
        {
            case Windows.System.VirtualKey.C when ctrl:
                CopyMeasurements();
                return true;
            case Windows.System.VirtualKey.Enter when !ctrl:
                InsertMeasurements();
                return true;
            case Windows.System.VirtualKey.Back when !ctrl:
                RemoveLastMeasurement();
                return true;
            case Windows.System.VirtualKey.Delete when !ctrl:
                ClearMeasurements();
                return true;
            case Windows.System.VirtualKey.Escape:
                if (_measureDragStart is not null)
                {
                    _measureDragStart = null;
                    _measureLive = null;
                    Canvas.Invalidate();
                }
                else
                {
                    ClearMeasurements();
                }
                return true;
            default:
                return false;
        }
    }

    /// <summary>Vẽ số đo (theo toạ độ ảnh, canvas đã dời + zoom) - gọi cuối Canvas_PaintSurface.</summary>
    private void DrawMeasureOverlay(SKCanvas canvas)
    {
        if (!IsMeasuring)
        {
            return;
        }
        DropMeasurementsIfImageChanged();
        var all = _measureLive is { } live ? _measurements.Append(live) : _measurements;
        // Nhãn / nét giữ cỡ cố định trên màn hình dù zoom bao nhiêu.
        Ruler.Draw(canvas, all, (float)CurrentScale / _zoom, ImageRect);
        InvalidateRulers();
    }

    // ---- Thước kẻ ở mép trên / trái khung ảnh ----

    private void InvalidateRulers()
    {
        if (HorizontalRuler.Visibility == Visibility.Visible)
        {
            FitRulers();
            HorizontalRuler.Invalidate();
            VerticalRuler.Invalidate();
        }
    }

    /// <summary>Thước dài bằng khung ảnh nhưng theo số DIP nguyên (CanvasSize): để lưới co giãn thì bitmap của SKXamlCanvas thiếu
    /// 1 px rồi bị kéo giãn → vạch thước nhoè. Gọi khi khung ảnh đổi cỡ và mỗi lần vẽ lại thước (đổi DPI khi kéo sang màn khác).</summary>
    private void FitRulers()
    {
        double scale = CurrentScale;
        double width = CanvasSize.ExactDip(CanvasScroller.ActualWidth * scale, scale);
        double height = CanvasSize.ExactDip(CanvasScroller.ActualHeight * scale, scale);
        if (HorizontalRuler.Width != width) HorizontalRuler.Width = width;
        if (VerticalRuler.Height != height) VerticalRuler.Height = height;
    }

    private void CanvasScroller_SizeChanged(object sender, SizeChangedEventArgs e) => InvalidateRulers();

    private void CanvasScroller_ViewChanged(object? sender, Microsoft.UI.Xaml.Controls.ScrollViewerViewChangedEventArgs e) => InvalidateRulers();

    private void HorizontalRuler_PaintSurface(object? sender, SKPaintSurfaceEventArgs e) => DrawRuler(e, horizontal: true);
    private void VerticalRuler_PaintSurface(object? sender, SKPaintSurfaceEventArgs e) => DrawRuler(e, horizontal: false);

    /// <summary>
    /// 1 thước kẻ (pixel vật lý): phần ứng với ảnh nền sáng hơn, vạch có số mỗi bước "đẹp" (1 / 2 / 5 × 10^k) cách nhau ≥ 50
    /// px màn hình, vạch nhỏ ở giữa; khoảng đang đo tô hồng nhạt, vị trí con trỏ = vạch hồng. Vạch mọc từ mép sát ảnh.
    /// </summary>
    private void DrawRuler(SKPaintSurfaceEventArgs e, bool horizontal)
    {
        var canvas = e.Surface.Canvas;
        float length = horizontal ? e.Info.Width : e.Info.Height;
        float thick = horizontal ? e.Info.Height : e.Info.Width;
        float scale = (float)CurrentScale;
        bool dark = (Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark;
        canvas.Clear(dark ? new SKColor(0x20, 0x20, 0x20) : new SKColor(0xE6, 0xE6, 0xE6));
        if (_viewModel is null)
        {
            return;
        }

        // pixel ảnh v ↔ vị trí trên thước (pixel vật lý): cùng gốc với ScrollViewer (thước nằm sát mép trên / trái của nó).
        float origin = (horizontal ? _viewOrigin.X : _viewOrigin.Y)
                       - (float)((horizontal ? CanvasScroller.HorizontalOffset : CanvasScroller.VerticalOffset) * scale);
        float At(float v) => origin + v * _zoom;
        int imageLength = horizontal ? _viewModel.Bitmap.Width : _viewModel.Bitmap.Height;

        using var imagePaint = new SKPaint { Color = dark ? new SKColor(0x2E, 0x2E, 0x2E) : new SKColor(0xFB, 0xFB, 0xFB) };
        using var tickPaint = new SKPaint { Color = dark ? new SKColor(0x8C, 0x8C, 0x8C) : new SKColor(0x80, 0x80, 0x80), StrokeWidth = 1 };
        using var font = SKTypeface.FromFamilyName("Segoe UI");
        using var textPaint = new SKPaint
        {
            Color = dark ? new SKColor(0xC8, 0xC8, 0xC8) : new SKColor(0x4A, 0x4A, 0x4A),
            IsAntialias = true,
            Typeface = font,
            TextSize = 9.5f * scale,
        };
        using var spanPaint = new SKPaint { Color = Ruler.LineColor.WithAlpha(70) };
        using var cursorPaint = new SKPaint { Color = Ruler.LineColor, StrokeWidth = Math.Max(1, MathF.Round(scale)) };

        // (a: dọc theo thước, c0..c1: ngang qua thước, c = thick là mép sát ảnh)
        void Rect(float a0, float a1, float c0, float c1, SKPaint paint) =>
            canvas.DrawRect(horizontal ? new SKRect(a0, c0, a1, c1) : new SKRect(c0, a0, c1, a1), paint);
        void Line(float a, float c0, float c1, SKPaint paint)
        {
            a = MathF.Floor(a) + 0.5f;
            if (horizontal) canvas.DrawLine(a, c0, a, c1, paint);
            else canvas.DrawLine(c0, a, c1, a, paint);
        }

        Rect(At(0), At(imageLength), 0, thick, imagePaint);

        var span = _measureLive ?? _measurements.LastOrDefault();
        // Dò cạnh chỉ 1 chiều: thước của chiều kia không tô khoảng đo.
        if (span is not null && span.EdgeAxis != (horizontal ? RulerAxis.Vertical : RulerAxis.Horizontal))
        {
            int lo = horizontal ? Math.Min(span.X1, span.X2) : Math.Min(span.Y1, span.Y2);
            int hi = horizontal ? Math.Max(span.X1, span.X2) : Math.Max(span.Y1, span.Y2);
            float end = span.Mode == RulerMode.Line ? hi + 0.5f : hi + 1;
            float start = span.Mode == RulerMode.Line ? lo + 0.5f : lo;
            Rect(At(start), At(end), 0, thick, spanPaint);
        }

        int step = Ruler.TickStep(_zoom, 50 * scale);
        int minor = Ruler.MinorTickStep(step, _zoom, 5 * scale);
        int every = minor > 0 ? minor : step;
        // Chỉ vạch trong ảnh (0..dài ảnh) và trong phần thước đang thấy.
        int first = Math.Max(0, (int)MathF.Ceiling(-origin / _zoom / every) * every);
        int last = Math.Min(imageLength, (int)((length - origin) / _zoom));
        for (int v = first; v <= last; v += every)
        {
            float a = At(v);
            bool major = v % step == 0;
            bool half = !major && minor > 0 && (step / minor) % 2 == 0 && v % (step / 2) == 0;
            float tickLength = major ? thick * 0.55f : half ? thick * 0.35f : thick * 0.2f;
            Line(a, thick - tickLength, thick, tickPaint);
            if (major)
            {
                string label = v.ToString();
                float baseline = -textPaint.FontMetrics.Ascent + 1 * scale;
                if (horizontal)
                {
                    canvas.DrawText(label, a + 2 * scale, baseline, textPaint);
                }
                else
                {
                    // Thước dọc: chữ xoay đọc từ dưới lên (như Photoshop / PicPick).
                    canvas.Save();
                    canvas.Translate(baseline, a - 2 * scale);
                    canvas.RotateDegrees(-90);
                    canvas.DrawText(label, 0, 0, textPaint);
                    canvas.Restore();
                }
            }
        }
        // Đường viền mép sát ảnh.
        float edge = thick - 0.5f;
        if (horizontal) canvas.DrawLine(0, edge, length, edge, tickPaint);
        else canvas.DrawLine(edge, 0, edge, length, tickPaint);

        if (_measureCursor is { } cursor)
        {
            Line(At((horizontal ? cursor.X : cursor.Y) + 0.5f), 0, thick, cursorPaint);
        }
    }
}
