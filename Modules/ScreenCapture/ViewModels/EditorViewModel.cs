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

    /// <summary>Đổi khung ảnh thành <paramref name="newRect"/> (toạ độ theo ảnh hiện tại, có thể âm hoặc
    /// vượt kích thước ảnh). Phần mới mở rộng tô trắng như PicPick.</summary>
    public void ResizeCanvas(SKRectI newRect)
    {
        if (newRect.Width < 1 || newRect.Height < 1)
        {
            return;
        }
        var info = new SKImageInfo(newRect.Width, newRect.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var resized = new SKBitmap(info);
        using (var canvas = new SKCanvas(resized))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(Bitmap, -newRect.Left, -newRect.Top);
        }
        UndoRedo.Do(new ResizeCanvasCommand(newBitmap => Bitmap = newBitmap, Annotations, Bitmap, resized,
            -newRect.Left, -newRect.Top));
    }

    // ---- Tool Select: thao tác trên vùng chọn (toạ độ pixel ảnh, đã kẹp trong khung ảnh) ----

    /// <summary>Copy đúng những gì đang thấy trong vùng (ảnh nền + annotation) vào clipboard.</summary>
    public async Task CopyRegionAsync(SKRectI region)
    {
        using var composited = RenderComposited();
        var part = new SKBitmap(new SKImageInfo(region.Width, region.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(part))
        {
            canvas.DrawBitmap(composited, SKRect.Create(region.Left, region.Top, region.Width, region.Height),
                SKRect.Create(region.Width, region.Height));
        }
        await _clipboardService.CopyBitmapAsync(part);
        StatusText = $"Đã copy vùng {region.Width} × {region.Height} px vào clipboard.";
    }

    /// <summary>Đọc ảnh trong clipboard và dán thành <see cref="ImageAnnotation"/> tại
    /// <paramref name="topLeft"/> (toạ độ ảnh). Ảnh dán vượt khung ảnh hiện tại → nới khung cho vừa (nền
    /// trắng), gộp chung 1 bước Undo với việc dán. Trả về shape vừa dán, null nếu clipboard không có ảnh.</summary>
    public async Task<ImageAnnotation?> PasteFromClipboardAsync(SKPoint topLeft)
    {
        var image = await _clipboardService.GetBitmapAsync();
        if (image is null)
        {
            StatusText = "Clipboard không có ảnh để dán.";
            return null;
        }

        var shape = new ImageAnnotation(image)
        {
            Bounds = SKRect.Create(topLeft.X, topLeft.Y, image.Width, image.Height),
        };
        var imageRect = SKRectI.Create(Bitmap.Width, Bitmap.Height);
        var needed = SKRectI.Union(imageRect, SKRectI.Ceiling(shape.Bounds));

        if (needed == imageRect)
        {
            AddAnnotation(shape);
        }
        else
        {
            // Nới khung chỉ về phải/dưới (topLeft luôn >= 0) → offset annotation = 0, shape giữ nguyên toạ độ.
            var resized = new SKBitmap(new SKImageInfo(needed.Width, needed.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(resized))
            {
                canvas.Clear(SKColors.White);
                canvas.DrawBitmap(Bitmap, 0, 0);
            }
            UndoRedo.Do(new CompositeEditCommand(
            [
                new ResizeCanvasCommand(newBitmap => Bitmap = newBitmap, Annotations, Bitmap, resized, 0, 0),
                new AddAnnotationCommand(Annotations, shape),
            ], "Dán ảnh"));
        }

        StatusText = $"Đã dán ảnh {image.Width} × {image.Height} px.";
        return shape;
    }

    public void EraseRegion(SKRectI region) =>
        UndoRedo.Do(new EraseRegionCommand(newBitmap => Bitmap = newBitmap, Bitmap, region));

    public void FloodFill(SKPointI seed) =>
        UndoRedo.Do(new FloodFillCommand(newBitmap => Bitmap = newBitmap, Bitmap, seed, StrokeColor));

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => UndoRedo.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => UndoRedo.Redo();

    /// <summary>Tên tab của ảnh trong Editor (thời điểm chụp, kiểu PicPick "2026-09-24 13 36 14").</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Định danh ổn định của tab - tên file lưu tạm phiên làm việc (SessionService).</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    private bool _savedToFile;
    public bool SavedToFile => _savedToFile;

    /// <summary>Nạp lại shape + trạng thái đã lưu của 1 tab từ phiên trước. Không đi qua Undo (mở lại
    /// app không có lịch sử Undo), shape vẫn chỉnh sửa được như bình thường.</summary>
    public void RestoreFromSession(IEnumerable<AnnotationShape> shapes, bool savedToFile)
    {
        foreach (var shape in shapes)
        {
            Annotations.Add(shape);
        }
        _savedToFile = savedToFile;
    }

    /// <summary>Ảnh chưa từng lưu ra file, hoặc đã sửa sau lần lưu gần nhất → đóng tab/cửa sổ phải hỏi,
    /// tránh mất ảnh chụp (kể cả ảnh vừa chụp chưa sửa gì).</summary>
    public bool NeedsSave => !_savedToFile || UndoRedo.IsDirty;

    [RelayCommand]
    private async Task SaveAsync() => await SaveToFileAsync();

    /// <summary>Lưu PNG (ảnh + shape) vào thư mục đã chọn sẵn, tên file = tên tab. Dùng cho "Lưu tất cả".</summary>
    public string SaveToFolder(string folder)
    {
        var path = _fileService.SavePngToFolder(RenderComposited(), folder, Title);
        _savedToFile = true;
        UndoRedo.MarkClean();
        StatusText = $"Đã lưu: {path}";
        return path;
    }

    /// <summary>Hỏi đường dẫn và lưu PNG. Trả false nếu người dùng huỷ hộp thoại lưu.</summary>
    public async Task<bool> SaveToFileAsync()
    {
        var path = await _fileService.SaveAsPngAsync(RenderComposited(), _ownerHwnd);
        if (path is null)
        {
            StatusText = "Đã huỷ lưu.";
            return false;
        }
        _savedToFile = true;
        UndoRedo.MarkClean();
        StatusText = $"Đã lưu: {path}";
        return true;
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
