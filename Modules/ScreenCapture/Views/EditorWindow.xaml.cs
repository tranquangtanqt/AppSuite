using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ScreenCapture.Models;
using ScreenCapture.Services;
using ScreenCapture.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using Windows.Foundation;
using WinRT.Interop;

namespace ScreenCapture.Views;

/// <summary>1 mục trong flyout chọn Stamps - Brush dùng cho preview Number stamp (hình tròn màu) và
/// làm màu FontIcon preview của General stamp; Glyph rỗng cho Number stamps (preview vẽ bằng Ellipse
/// trong XAML thay vì icon).</summary>
public sealed class StampPickerItem
{
    public StampKind Kind { get; set; }
    public SolidColorBrush Brush { get; set; } = new(Microsoft.UI.Colors.Black);
    public string Glyph { get; set; } = string.Empty;
}

/// <summary>Owns all pointer/canvas interaction (View responsibility, same split CsvEditor's
/// MainWindow uses) - EditorViewModel itself never references WinUI/Skia UI types beyond SKBitmap.</summary>
public sealed partial class EditorWindow : Window
{
    private readonly EditorViewModel _viewModel;
    private AnnotationShape? _draftShape;
    private SKPoint _dragStartPoint;
    private bool _isCropping;
    private SKRect _cropRect;

    private StampKind? _selectedStampKind;
    private SKColor _selectedStampColor;
    private int _numberStampCounter = 1;

    // Move tool drag state.
    private AnnotationShape? _movingShape;
    private SKRect _movingOldBounds;
    private SKPoint _moveDragStart;
    private int _resizingHandle = -1; // -1 = none, 0..3 = TL/TR/BL/BR

    public ObservableCollection<StampPickerItem> NumberStamps { get; } = [];
    public ObservableCollection<StampPickerItem> GeneralStamps { get; } = [];

    // StampsToolButton is a plain Button (Flyout is Button-only in WinUI3, ToggleButton has no
    // Flyout property) - nó không có IsChecked nên không nằm trong danh sách bật/tắt dưới đây.
    private List<ToggleButton> ToolButtons => [
        MoveToolButton, RectangleToolButton, EllipseToolButton, LineToolButton, ArrowToolButton,
        HighlightToolButton, TextToolButton, FillToolButton,
    ];

    public EditorWindow(SKBitmap bitmap, IImageFileService fileService, IClipboardService clipboardService)
    {
        InitializeComponent();
        var hwnd = WindowNative.GetWindowHandle(this);
        _viewModel = new EditorViewModel(bitmap, fileService, clipboardService, hwnd);
        _viewModel.RequestRedraw += (_, _) => Canvas.Invalidate();
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorViewModel.Bitmap))
            {
                Canvas.Width = _viewModel.Bitmap.Width;
                Canvas.Height = _viewModel.Bitmap.Height;
                Canvas.Invalidate();
            }
            if (e.PropertyName == nameof(EditorViewModel.StatusText))
            {
                StatusText.Text = _viewModel.StatusText;
            }
            if (e.PropertyName == nameof(EditorViewModel.SelectedAnnotation))
            {
                bool hasSelection = _viewModel.SelectedAnnotation is not null;
                DeleteButton.IsEnabled = hasSelection;
                BringToFrontButton.IsEnabled = hasSelection;
                SendToBackButton.IsEnabled = hasSelection;
                UpdateNumberStampTab();
            }
        };

        this.Content.KeyDown += Content_KeyDown;

        // Mở full màn hình (maximize) cho dễ chỉnh sửa - ảnh chụp thường lớn hơn kích thước cửa sổ
        // mặc định, ScrollViewer bao Canvas xử lý phần còn lại nếu ảnh vẫn lớn hơn cả màn hình.
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }

        Canvas.Width = bitmap.Width;
        Canvas.Height = bitmap.Height;

        Color1ColorPicker.Color = ToWindowsColor(_viewModel.StrokeColor);
        Color2ColorPicker.Color = ToWindowsColor(_viewModel.FillColor);
        Color1Swatch.Fill = new SolidColorBrush(Color1ColorPicker.Color);
        Color2Swatch.Fill = new SolidColorBrush(Color2ColorPicker.Color);

        // Minimum/Maximum/Value gán qua XAML attribute từng gây XamlParseException lúc chạy
        // ("Failed to assign to property RangeBase.Minimum") - gán qua code-behind để tránh.
        SizeSlider.Minimum = 1;
        SizeSlider.Maximum = 20;
        SizeSlider.Value = 3;

        // NumberBox cùng họ RangeBase-like control - áp dụng lại bài học Slider ở trên, không set
        // Minimum/SmallChange qua XAML.
        CurrentNumberBox.Minimum = 1;
        CurrentNumberBox.SmallChange = 1;
        NextNumberBox.Minimum = 1;
        NextNumberBox.SmallChange = 1;
        NextNumberBox.Value = _numberStampCounter;

        PopulateStampPickers();
        SelectTool(CaptureTool.Rectangle, RectangleToolButton);
    }

    private static Windows.UI.Color ToWindowsColor(SKColor c) => Windows.UI.Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue);
    private static SKColor ToSkColor(Windows.UI.Color c) => new(c.R, c.G, c.B, c.A);

    private void PopulateStampPickers()
    {
        var numberBrushes = new[]
        {
            Windows.UI.Color.FromArgb(255, 0xD8, 0x64, 0x45), Colors.RoyalBlue, Colors.DarkOrange, Colors.SeaGreen,
            Colors.MediumPurple, Colors.DeepSkyBlue, Colors.DimGray,
        };
        foreach (var brush in numberBrushes)
        {
            NumberStamps.Add(new StampPickerItem { Kind = StampKind.Number, Brush = new SolidColorBrush(brush) });
        }

        var generalBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xD8, 0x64, 0x45));
        (StampKind Kind, string Glyph)[] general =
        [
            (StampKind.ArrowUp, ""), (StampKind.ArrowUpRight, ""), (StampKind.ArrowRight, ""),
            (StampKind.ArrowDownRight, ""), (StampKind.ArrowDown, ""), (StampKind.ArrowDownLeft, ""),
            (StampKind.ArrowLeft, ""), (StampKind.ArrowUpLeft, ""),
            (StampKind.Bookmark, ""), (StampKind.Pin, ""), (StampKind.Flag, ""), (StampKind.Tag, ""),
            (StampKind.Info, ""), (StampKind.Warning, ""), (StampKind.NoEntry, ""), (StampKind.Heart, ""),
            (StampKind.Plus, ""), (StampKind.Minus, ""), (StampKind.Check, ""), (StampKind.Cross, ""),
            (StampKind.Star, ""),
        ];
        foreach (var (kind, glyph) in general)
        {
            GeneralStamps.Add(new StampPickerItem { Kind = kind, Brush = generalBrush, Glyph = glyph });
        }
    }

    private void Canvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(_viewModel.Bitmap, 0, 0);
        foreach (var shape in _viewModel.Annotations)
        {
            shape.Render(canvas);
        }
        _draftShape?.Render(canvas);

        if (_viewModel.SelectedAnnotation is { } selected)
        {
            using var selectPaint = new SKPaint
            {
                Color = SKColors.DeepSkyBlue,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2,
                PathEffect = SKPathEffect.CreateDash([6, 4], 0),
            };
            canvas.DrawRect(selected.Bounds, selectPaint);

            using var handleFill = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
            using var handleStroke = new SKPaint { Color = SKColors.DeepSkyBlue, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
            (float X, float Y)[] corners =
            [
                (selected.Bounds.Left, selected.Bounds.Top), (selected.Bounds.Right, selected.Bounds.Top),
                (selected.Bounds.Left, selected.Bounds.Bottom), (selected.Bounds.Right, selected.Bounds.Bottom),
            ];
            foreach (var (x, y) in corners)
            {
                canvas.DrawRect(new SKRect(x - HandleSize / 2, y - HandleSize / 2, x + HandleSize / 2, y + HandleSize / 2), handleFill);
                canvas.DrawRect(new SKRect(x - HandleSize / 2, y - HandleSize / 2, x + HandleSize / 2, y + HandleSize / 2), handleStroke);
            }
        }

        if (_isCropping)
        {
            using var paint = new SKPaint { Color = SKColors.DeepSkyBlue, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
            canvas.DrawRect(_cropRect, paint);
        }
    }

    /// <summary>Pointer events trả toạ độ logic (DIP), nhưng SKXamlCanvas vẽ theo pixel vật lý
    /// (Canvas.Width/Height set = kích thước bitmap tính bằng pixel). Trên máy DPI scale khác 100%
    /// (rất phổ biến khi dùng nhiều màn hình), 2 hệ toạ độ này lệch nhau đúng bằng RasterizationScale
    /// - không nhân lại thì click sẽ vẽ/hit-test sai vị trí (đã gặp thực tế: stamp không nằm đúng chỗ
    /// click). Nhân theo RasterizationScale để quy về đúng không gian pixel mà SKCanvas dùng.</summary>
    private SKPoint ToCanvasPoint(Point p)
    {
        double scale = Content.XamlRoot?.RasterizationScale ?? 1.0;
        return new SKPoint((float)(p.X * scale), (float)(p.Y * scale));
    }

    private static SKRect Inflate(SKRect rect, float amount) =>
        new(rect.Left - amount, rect.Top - amount, rect.Right + amount, rect.Bottom + amount);

    private const float HandleSize = 8f;

    /// <summary>Trả về chỉ số handle (0=TL,1=TR,2=BL,3=BR) nếu pos rơi vào 1 trong 4 góc của bounds,
    /// null nếu không trúng handle nào.</summary>
    private static int? HitTestHandle(SKRect bounds, SKPoint pos)
    {
        (float X, float Y)[] corners =
        [
            (bounds.Left, bounds.Top), (bounds.Right, bounds.Top),
            (bounds.Left, bounds.Bottom), (bounds.Right, bounds.Bottom),
        ];
        for (int i = 0; i < corners.Length; i++)
        {
            if (Math.Abs(pos.X - corners[i].X) <= HandleSize && Math.Abs(pos.Y - corners[i].Y) <= HandleSize)
            {
                return i;
            }
        }
        return null;
    }

    private static SKRect ResizeFromHandle(SKRect original, int handleIndex, SKPoint pos)
    {
        float left = original.Left, top = original.Top, right = original.Right, bottom = original.Bottom;
        switch (handleIndex)
        {
            case 0: left = pos.X; top = pos.Y; break;
            case 1: right = pos.X; top = pos.Y; break;
            case 2: left = pos.X; bottom = pos.Y; break;
            case 3: right = pos.X; bottom = pos.Y; break;
        }
        return new SKRect(Math.Min(left, right), Math.Min(top, bottom), Math.Max(left, right), Math.Max(top, bottom));
    }

    private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pos = ToCanvasPoint(e.GetCurrentPoint(Canvas).Position);
        _dragStartPoint = pos;

        if (_isCropping)
        {
            _cropRect = new SKRect(pos.X, pos.Y, pos.X, pos.Y);
            Canvas.CapturePointer(e.Pointer);
            return;
        }

        switch (_viewModel.SelectedTool)
        {
            case CaptureTool.Move:
                if (_viewModel.SelectedAnnotation is { } selected && HitTestHandle(selected.Bounds, pos) is { } handleIndex)
                {
                    _resizingHandle = handleIndex;
                    _movingShape = selected;
                    _movingOldBounds = selected.Bounds;
                    Canvas.CapturePointer(e.Pointer);
                    break;
                }

                var hit = _viewModel.Annotations.Reverse().FirstOrDefault(s => Inflate(s.Bounds, 6).Contains(pos.X, pos.Y));
                _viewModel.SelectedAnnotation = hit;
                if (hit is not null)
                {
                    _movingShape = hit;
                    _movingOldBounds = hit.Bounds;
                    _moveDragStart = pos;
                    Canvas.CapturePointer(e.Pointer);
                }
                Canvas.Invalidate();
                break;

            case CaptureTool.Rectangle:
                _draftShape = new RectangleAnnotation { Bounds = new SKRect(pos.X, pos.Y, pos.X, pos.Y), Color = _viewModel.StrokeColor, StrokeWidth = _viewModel.StrokeWidth };
                Canvas.CapturePointer(e.Pointer);
                break;
            case CaptureTool.Ellipse:
                _draftShape = new EllipseAnnotation { Bounds = new SKRect(pos.X, pos.Y, pos.X, pos.Y), Color = _viewModel.StrokeColor, StrokeWidth = _viewModel.StrokeWidth };
                Canvas.CapturePointer(e.Pointer);
                break;
            case CaptureTool.Line:
                _draftShape = new LineArrowAnnotation { Bounds = new SKRect(pos.X, pos.Y, pos.X, pos.Y), Color = _viewModel.StrokeColor, StrokeWidth = _viewModel.StrokeWidth, IsArrow = false };
                Canvas.CapturePointer(e.Pointer);
                break;
            case CaptureTool.Arrow:
                _draftShape = new LineArrowAnnotation { Bounds = new SKRect(pos.X, pos.Y, pos.X, pos.Y), Color = _viewModel.StrokeColor, StrokeWidth = _viewModel.StrokeWidth };
                Canvas.CapturePointer(e.Pointer);
                break;
            case CaptureTool.Highlight:
                _draftShape = new HighlightAnnotation { Bounds = new SKRect(pos.X, pos.Y, pos.X, pos.Y), Color = _viewModel.FillColor.WithAlpha(90) };
                Canvas.CapturePointer(e.Pointer);
                break;
            case CaptureTool.Text:
                PromptForText(pos);
                break;
            case CaptureTool.Fill:
                _viewModel.FloodFill(new SKPointI((int)pos.X, (int)pos.Y));
                break;
            case CaptureTool.Stamp:
                if (_selectedStampKind is { } kind)
                {
                    const float half = 16f;
                    var shape = new StampAnnotation
                    {
                        Kind = kind,
                        Color = _selectedStampColor,
                        // Number stamps: số tự tăng dần mỗi lần đặt (giống PicPick), không cố định.
                        NumberValue = kind == StampKind.Number ? _numberStampCounter++ : 0,
                        Bounds = new SKRect(pos.X - half, pos.Y - half, pos.X + half, pos.Y + half),
                    };
                    _viewModel.AddAnnotation(shape);
                    if (kind == StampKind.Number)
                    {
                        NextNumberBox.Value = _numberStampCounter;
                    }
                }
                break;
        }
    }

    private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pos = ToCanvasPoint(e.GetCurrentPoint(Canvas).Position);

        if (_isCropping && e.GetCurrentPoint(Canvas).Properties.IsLeftButtonPressed)
        {
            _cropRect = MakeRect(_dragStartPoint, pos);
            Canvas.Invalidate();
            return;
        }

        if (_movingShape is not null && _resizingHandle >= 0)
        {
            _movingShape.Bounds = ResizeFromHandle(_movingOldBounds, _resizingHandle, pos);
            Canvas.Invalidate();
            return;
        }

        if (_movingShape is not null)
        {
            float dx = pos.X - _moveDragStart.X;
            float dy = pos.Y - _moveDragStart.Y;
            _movingShape.Bounds = new SKRect(
                _movingOldBounds.Left + dx, _movingOldBounds.Top + dy,
                _movingOldBounds.Right + dx, _movingOldBounds.Bottom + dy);
            Canvas.Invalidate();
            return;
        }

        if (_draftShape is null)
        {
            return;
        }
        _draftShape.Bounds = MakeRect(_dragStartPoint, pos);
        Canvas.Invalidate();
    }

    private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        Canvas.ReleasePointerCapture(e.Pointer);

        if (_isCropping)
        {
            _isCropping = false;
            CropToolButton.IsChecked = false;
            if (_cropRect.Width > 2 && _cropRect.Height > 2)
            {
                _viewModel.Crop(_cropRect);
            }
            Canvas.Invalidate();
            return;
        }

        if (_movingShape is not null)
        {
            var newBounds = _movingShape.Bounds;
            _movingShape.Bounds = _movingOldBounds; // MoveResizeAnnotation's Execute() re-applies newBounds
            _viewModel.MoveResizeAnnotation(_movingShape, _movingOldBounds, newBounds);
            _movingShape = null;
            _resizingHandle = -1;
            Canvas.Invalidate();
            return;
        }

        if (_draftShape is null)
        {
            return;
        }

        if (_draftShape.Bounds.Width > 2 || _draftShape.Bounds.Height > 2)
        {
            _viewModel.AddAnnotation(_draftShape);
        }
        _draftShape = null;
        Canvas.Invalidate();
    }

    private static SKRect MakeRect(SKPoint a, SKPoint b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    private async void PromptForText(SKPoint position)
    {
        var textBox = new TextBox { PlaceholderText = "Nhập text...", Width = 240 };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Thêm text",
            Content = textBox,
            PrimaryButtonText = "Thêm",
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(textBox.Text))
        {
            var shape = new TextAnnotation
            {
                Bounds = new SKRect(position.X, position.Y, position.X + 200, position.Y + 30),
                Text = textBox.Text,
                Color = _viewModel.StrokeColor,
            };
            _viewModel.AddAnnotation(shape);
        }
    }

    private void Content_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is Windows.System.VirtualKey.Delete or Windows.System.VirtualKey.Back &&
            _viewModel.SelectedAnnotation is not null)
        {
            _viewModel.DeleteSelectedAnnotation();
            Canvas.Invalidate();
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DeleteSelectedAnnotation();
        Canvas.Invalidate();
    }

    private void BringToFrontButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedAnnotation is { } shape)
        {
            _viewModel.BringToFront(shape);
            Canvas.Invalidate();
        }
    }

    private void SendToBackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedAnnotation is { } shape)
        {
            _viewModel.SendToBack(shape);
            Canvas.Invalidate();
        }
    }

    private void MoveToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Move, MoveToolButton);
    private void RectangleToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Rectangle, RectangleToolButton);
    private void EllipseToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Ellipse, EllipseToolButton);
    private void LineToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Line, LineToolButton);
    private void ArrowToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Arrow, ArrowToolButton);
    private void HighlightToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Highlight, HighlightToolButton);
    private void TextToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Text, TextToolButton);
    private void FillToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Fill, FillToolButton);

    private void StampItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: StampKind kind })
        {
            _selectedStampKind = kind;
            _selectedStampColor = _viewModel.StrokeColor;
            SelectTool(CaptureTool.Stamp, StampsToolButton);
            StampsFlyout.Hide();
        }
    }

    private void NumberStampItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SolidColorBrush brush })
        {
            _selectedStampKind = StampKind.Number;
            _selectedStampColor = ToSkColor(brush.Color);
            SelectTool(CaptureTool.Stamp, StampsToolButton);
            StampsFlyout.Hide();
        }
    }

    private void SelectTool(CaptureTool tool, ButtonBase? pressedButton)
    {
        _viewModel.SelectedTool = tool;
        _viewModel.SelectedAnnotation = null;
        _isCropping = false;
        CropToolButton.IsChecked = false;
        foreach (var btn in ToolButtons)
        {
            btn.IsChecked = ReferenceEquals(btn, pressedButton);
        }
        Canvas.Invalidate();
    }

    private void CropToolButton_Click(object sender, RoutedEventArgs e)
    {
        _isCropping = CropToolButton.IsChecked == true;
        if (_isCropping)
        {
            _viewModel.SelectedTool = CaptureTool.None;
            _viewModel.SelectedAnnotation = null;
            foreach (var btn in ToolButtons)
            {
                btn.IsChecked = false;
            }
        }
    }

    private void SizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _viewModel.StrokeWidth = (float)e.NewValue;
        ApplyStyleToSelectionIfAny();
    }

    private void Color1ColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        _viewModel.StrokeColor = ToSkColor(args.NewColor);
        Color1Swatch.Fill = new SolidColorBrush(args.NewColor);
        ApplyStyleToSelectionIfAny();
    }

    private void Color2ColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        _viewModel.FillColor = ToSkColor(args.NewColor);
        Color2Swatch.Fill = new SolidColorBrush(args.NewColor);
        ApplyStyleToSelectionIfAny();
    }

    /// <summary>Khi đang có 1 shape được chọn (Move tool), đổi Color1/Size áp dụng luôn lên shape đó
    /// thay vì chỉ ảnh hưởng shape vẽ tiếp theo - đúng hành vi "sửa lại shape đã đặt" người dùng yêu
    /// cầu. Color2 (fill) chỉ có ý nghĩa với Highlight nên không áp cho shape khác qua đường này.</summary>
    private void ApplyStyleToSelectionIfAny()
    {
        if (_viewModel.SelectedAnnotation is { } shape)
        {
            var color = shape is HighlightAnnotation ? _viewModel.FillColor.WithAlpha(90) : _viewModel.StrokeColor;
            _viewModel.ChangeAnnotationStyle(shape, color, _viewModel.StrokeWidth);
            Canvas.Invalidate();
        }
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e) => _viewModel.UndoCommand.Execute(null);
    private void RedoButton_Click(object sender, RoutedEventArgs e) => _viewModel.RedoCommand.Execute(null);
    private void SaveButton_Click(object sender, RoutedEventArgs e) => _viewModel.SaveCommand.Execute(null);
    private void CopyButton_Click(object sender, RoutedEventArgs e) => _viewModel.CopyToClipboardCommand.Execute(null);
    private void CloseButton_Click(object sender, RoutedEventArgs e) => this.Close();

    private enum RibbonTab { Home, File, NumberStamp }

    private void RibbonTabHeader_Click(object sender, RoutedEventArgs e)
    {
        var tab = sender switch
        {
            _ when ReferenceEquals(sender, FileTabHeader) => RibbonTab.File,
            _ when ReferenceEquals(sender, NumberStampTabHeader) => RibbonTab.NumberStamp,
            _ => RibbonTab.Home,
        };
        SelectRibbonTab(tab);
    }

    private void SelectRibbonTab(RibbonTab tab)
    {
        HomeTabHeader.IsChecked = tab == RibbonTab.Home;
        FileTabHeader.IsChecked = tab == RibbonTab.File;
        NumberStampTabHeader.IsChecked = tab == RibbonTab.NumberStamp;
        HomeRibbonPanel.Visibility = tab == RibbonTab.Home ? Visibility.Visible : Visibility.Collapsed;
        FileRibbonPanel.Visibility = tab == RibbonTab.File ? Visibility.Visible : Visibility.Collapsed;
        NumberStampRibbonPanel.Visibility = tab == RibbonTab.NumberStamp ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Hiện/ẩn tab contextual "Number Stamp" theo SelectedAnnotation (giống PicPick: chọn 1
    /// Number Stamp đã đặt tự nhảy sang tab này). Đồng bộ Current/Outline/Fill hiển thị theo đúng
    /// shape đang chọn - các handler ValueChanged/ColorChanged bên dưới không tạo command thừa vì
    /// EditorViewModel chỉ Do() khi giá trị mới thực sự khác giá trị cũ.</summary>
    private void UpdateNumberStampTab()
    {
        if (_viewModel.SelectedAnnotation is StampAnnotation { Kind: StampKind.Number } stamp)
        {
            NumberStampTabHeader.Visibility = Visibility.Visible;
            CurrentNumberBox.Value = stamp.NumberValue;
            StampFillSwatch.Fill = new SolidColorBrush(ToWindowsColor(stamp.Color));
            StampFillColorPicker.Color = ToWindowsColor(stamp.Color);
            StampOutlineSwatch.Fill = new SolidColorBrush(ToWindowsColor(stamp.OutlineColor));
            StampOutlineColorPicker.Color = ToWindowsColor(stamp.OutlineColor);
            SelectRibbonTab(RibbonTab.NumberStamp);
        }
        else
        {
            NumberStampTabHeader.Visibility = Visibility.Collapsed;
            if (NumberStampTabHeader.IsChecked == true)
            {
                SelectRibbonTab(RibbonTab.Home);
            }
        }
    }

    private void FlattenButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.FlattenSelectedAnnotation();
        Canvas.Invalidate();
    }

    private void StampStyleSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SolidColorBrush brush } &&
            _viewModel.SelectedAnnotation is StampAnnotation { Kind: StampKind.Number } stamp)
        {
            _viewModel.ChangeStampColors(stamp, ToSkColor(brush.Color), stamp.OutlineColor);
            StampFillSwatch.Fill = new SolidColorBrush(brush.Color);
            StampFillColorPicker.Color = brush.Color;
            Canvas.Invalidate();
        }
    }

    private void CurrentNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_viewModel.SelectedAnnotation is StampAnnotation { Kind: StampKind.Number } stamp && !double.IsNaN(args.NewValue))
        {
            _viewModel.ChangeStampNumber(stamp, (int)args.NewValue);
            Canvas.Invalidate();
        }
    }

    private void NextNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!double.IsNaN(args.NewValue))
        {
            _numberStampCounter = (int)args.NewValue;
        }
    }

    private void StampOutlineColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_viewModel.SelectedAnnotation is StampAnnotation { Kind: StampKind.Number } stamp)
        {
            _viewModel.ChangeStampColors(stamp, stamp.Color, ToSkColor(args.NewColor));
            StampOutlineSwatch.Fill = new SolidColorBrush(args.NewColor);
            Canvas.Invalidate();
        }
    }

    private void StampFillColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_viewModel.SelectedAnnotation is StampAnnotation { Kind: StampKind.Number } stamp)
        {
            _viewModel.ChangeStampColors(stamp, ToSkColor(args.NewColor), stamp.OutlineColor);
            StampFillSwatch.Fill = new SolidColorBrush(args.NewColor);
            Canvas.Invalidate();
        }
    }
}
