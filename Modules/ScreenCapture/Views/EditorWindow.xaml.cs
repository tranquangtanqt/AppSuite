using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using ScreenCapture.Models;
using ScreenCapture.Services;
using ScreenCapture.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using Windows.Foundation;
using WinRT.Interop;

namespace ScreenCapture.Views;

/// <summary>Owns all pointer/canvas interaction (View responsibility, same split CsvEditor's
/// MainWindow uses) - EditorViewModel itself never references WinUI/Skia UI types beyond SKBitmap.</summary>
public sealed partial class EditorWindow : Window
{
    private readonly EditorViewModel _viewModel;
    private AnnotationShape? _draftShape;
    private SKPoint _dragStartPoint;
    private bool _isCropping;
    private SKRect _cropRect;

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
        };

        Canvas.Width = bitmap.Width;
        Canvas.Height = bitmap.Height;
        RectangleToolButton.IsChecked = true;
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

        if (_isCropping)
        {
            using var paint = new SKPaint { Color = SKColors.DeepSkyBlue, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
            canvas.DrawRect(_cropRect, paint);
        }
    }

    private SKPoint ToCanvasPoint(Point p) => new((float)p.X, (float)p.Y);

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
            case CaptureTool.Rectangle:
                _draftShape = new RectangleAnnotation { Bounds = new SKRect(pos.X, pos.Y, pos.X, pos.Y) };
                Canvas.CapturePointer(e.Pointer);
                break;
            case CaptureTool.Arrow:
                _draftShape = new LineArrowAnnotation { Bounds = new SKRect(pos.X, pos.Y, pos.X, pos.Y) };
                Canvas.CapturePointer(e.Pointer);
                break;
            case CaptureTool.Text:
                PromptForText(pos);
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
            };
            _viewModel.AddAnnotation(shape);
        }
    }

    private void RectangleToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Rectangle, RectangleToolButton);
    private void ArrowToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Arrow, ArrowToolButton);
    private void TextToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Text, TextToolButton);

    private void SelectTool(CaptureTool tool, ToggleButton pressedButton)
    {
        _viewModel.SelectedTool = tool;
        _isCropping = false;
        CropToolButton.IsChecked = false;
        foreach (var btn in new[] { RectangleToolButton, ArrowToolButton, TextToolButton })
        {
            btn.IsChecked = ReferenceEquals(btn, pressedButton);
        }
    }

    private void CropToolButton_Click(object sender, RoutedEventArgs e)
    {
        _isCropping = CropToolButton.IsChecked == true;
        if (_isCropping)
        {
            foreach (var btn in new[] { RectangleToolButton, ArrowToolButton, TextToolButton })
            {
                btn.IsChecked = false;
            }
        }
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e) => _viewModel.UndoCommand.Execute(null);
    private void RedoButton_Click(object sender, RoutedEventArgs e) => _viewModel.RedoCommand.Execute(null);
    private void SaveButton_Click(object sender, RoutedEventArgs e) => _viewModel.SaveCommand.Execute(null);
    private void CopyButton_Click(object sender, RoutedEventArgs e) => _viewModel.CopyToClipboardCommand.Execute(null);
}
