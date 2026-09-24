using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenCapture.Commands;
using ScreenCapture.Models;
using ScreenCapture.Services;
using SkiaSharp;

namespace ScreenCapture.ViewModels;

/// <summary>Owns the captured bitmap, the annotation list and the Undo/Redo stack. Never references
/// WinUI types - EditorWindow code-behind owns all pointer/canvas interaction (same split CsvEditor
/// uses between MainWindow.xaml.cs and CsvEditorViewModel).</summary>
public sealed partial class EditorViewModel : ObservableObject
{
    private readonly IImageFileService _fileService;
    private readonly IClipboardService _clipboardService;
    private readonly IntPtr _ownerHwnd;

    public EditorViewModel(SKBitmap bitmap, IImageFileService fileService, IClipboardService clipboardService,
        IntPtr ownerHwnd)
    {
        _bitmap = bitmap;
        _fileService = fileService;
        _clipboardService = clipboardService;
        _ownerHwnd = ownerHwnd;
        UndoRedo.StateChanged += (_, _) =>
        {
            // Undo "vẽ shape" / Redo "xoá shape" khi shape đó đang được chọn → bỏ chọn, tránh vẽ handle
            // và tab contextual cho shape không còn trên canvas.
            if (SelectedAnnotation is { } selected && !Annotations.Contains(selected))
            {
                SelectedAnnotation = null;
            }
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            RequestRedraw?.Invoke(this, EventArgs.Empty);
        };
    }

    public UndoRedoStack UndoRedo { get; } = new();
    public ObservableCollection<AnnotationShape> Annotations { get; } = [];

    /// <summary>Raised whenever the canvas needs to repaint (state change, or a shape is actively
    /// being dragged). EditorWindow subscribes and calls SKXamlCanvas.Invalidate().</summary>
    public event EventHandler? RequestRedraw;

    [ObservableProperty]
    private SKBitmap _bitmap;

    [ObservableProperty]
    private CaptureTool _selectedTool = CaptureTool.Rectangle;

    [ObservableProperty]
    private string _statusText = "Sẵn sàng.";

    /// <summary>Color1 (PicPick convention) - stroke/màu chính, dùng khi vẽ shape mới hoặc Fill.
    /// Đỏ cam nhạt (không phải đỏ thuần FF0000) - đúng tông màu mặc định của PicPick.</summary>
    [ObservableProperty]
    private SKColor _strokeColor = new(0xE7, 0x4C, 0x3C);

    /// <summary>Color2 (PicPick convention) - fill/màu phụ, dùng cho Highlight và các shape có tô nền.</summary>
    [ObservableProperty]
    private SKColor _fillColor = SKColors.White;

    [ObservableProperty]
    private float _strokeWidth = 3f;

    /// <summary>Shape đang được chọn bởi Move tool - EditorWindow vẽ viền chấm chấm quanh nó.</summary>
    [ObservableProperty]
    private AnnotationShape? _selectedAnnotation;

    public bool CanUndo => UndoRedo.CanUndo;
    public bool CanRedo => UndoRedo.CanRedo;

    public void AddAnnotation(AnnotationShape shape) =>
        UndoRedo.Do(new AddAnnotationCommand(Annotations, shape));

    public void RemoveAnnotation(AnnotationShape shape) =>
        UndoRedo.Do(new RemoveAnnotationCommand(Annotations, shape));

    public void MoveResizeAnnotation(AnnotationShape shape, SKRect oldBounds, SKRect newBounds) =>
        UndoRedo.Do(new MoveResizeAnnotationCommand(shape, oldBounds, newBounds));

    public void EditText(TextAnnotation shape, string newText) =>
        UndoRedo.Do(new EditTextAnnotationCommand(shape, shape.Text, newText));

    public void DeleteSelectedAnnotation()
    {
        if (SelectedAnnotation is { } shape)
        {
            RemoveAnnotation(shape);
            SelectedAnnotation = null;
        }
    }

    public void BringToFront(AnnotationShape shape)
    {
        int oldIndex = Annotations.IndexOf(shape);
        int newIndex = Annotations.Count - 1;
        if (oldIndex >= 0 && oldIndex != newIndex)
        {
            UndoRedo.Do(new ReorderAnnotationCommand(Annotations, oldIndex, newIndex));
        }
    }

    public void SendToBack(AnnotationShape shape)
    {
        int oldIndex = Annotations.IndexOf(shape);
        if (oldIndex > 0)
        {
            UndoRedo.Do(new ReorderAnnotationCommand(Annotations, oldIndex, 0));
        }
    }

    public void ChangeAnnotationStyle(AnnotationShape shape, SKColor newColor, float newStrokeWidth)
    {
        if (shape.Color != newColor || shape.StrokeWidth != newStrokeWidth)
        {
            UndoRedo.Do(new ChangeAnnotationStyleCommand(shape, shape.Color, newColor, shape.StrokeWidth, newStrokeWidth));
        }
    }

    public void ChangeStampColors(StampAnnotation shape, SKColor newFill, SKColor newOutline)
    {
        if (shape.Color != newFill || shape.OutlineColor != newOutline)
        {
            UndoRedo.Do(new ChangeStampColorsCommand(shape, shape.Color, newFill, shape.OutlineColor, newOutline));
        }
    }

    public void ChangeStampNumber(StampAnnotation shape, int newValue)
    {
        if (shape.NumberValue != newValue)
        {
            UndoRedo.Do(new ChangeStampNumberCommand(shape, shape.NumberValue, newValue));
        }
    }

    public void FlattenSelectedAnnotation()
    {
        if (SelectedAnnotation is { } shape)
        {
            UndoRedo.Do(new FlattenAnnotationCommand(newBitmap => Bitmap = newBitmap, Annotations, Bitmap, shape));
            SelectedAnnotation = null;
        }
    }

    public void RequestRedrawNow() => RequestRedraw?.Invoke(this, EventArgs.Empty);

    public void Crop(SKRect cropRect)
    {
        var info = new SKImageInfo((int)cropRect.Width, (int)cropRect.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var cropped = new SKBitmap(info);
        using (var canvas = new SKCanvas(cropped))
        {
            canvas.DrawBitmap(Bitmap, cropRect, new SKRect(0, 0, cropRect.Width, cropRect.Height));
        }

        UndoRedo.Do(new CropCommand(newBitmap => Bitmap = newBitmap, Annotations, Bitmap, cropped, cropRect));
    }

    public void FloodFill(SKPointI seed) =>
        UndoRedo.Do(new FloodFillCommand(newBitmap => Bitmap = newBitmap, Bitmap, seed, StrokeColor));

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => UndoRedo.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => UndoRedo.Redo();

    [RelayCommand]
    private async Task SaveAsync()
    {
        var path = await _fileService.SaveAsPngAsync(RenderComposited(), _ownerHwnd);
        StatusText = path is null ? "Đã huỷ lưu." : $"Đã lưu: {path}";
    }

    [RelayCommand]
    private async Task CopyToClipboardAsync()
    {
        await _clipboardService.CopyBitmapAsync(RenderComposited());
        StatusText = "Đã copy vào clipboard.";
    }

    /// <summary>Flattens the base bitmap + every annotation into one bitmap for Save/Copy.</summary>
    public SKBitmap RenderComposited()
    {
        var info = new SKImageInfo(Bitmap.Width, Bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var result = new SKBitmap(info);
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(Bitmap, 0, 0);
        foreach (var shape in Annotations)
        {
            shape.Render(canvas);
        }
        return result;
    }
}
