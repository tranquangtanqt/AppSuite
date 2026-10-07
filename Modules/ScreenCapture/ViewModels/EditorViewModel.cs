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

    /// <summary>Ghi 1 thay đổi đã làm trên shape (di chuyển, đổi màu / nét / phông, sửa chữ...) thành 1 bước Undo.
    /// <paramref name="before"/> / <paramref name="after"/> là <see cref="AnnotationShape.Snapshot"/> trước và sau khi đổi.</summary>
    public void ChangeShape(AnnotationShape shape, AnnotationShape before, AnnotationShape after, string description) =>
        UndoRedo.Do(new ChangeShapeCommand(shape, before, after, description));

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

    // ---- Cắt ảnh khôi phục được ----
    // Mỗi ảnh sinh ra từ Cắt / đổi khung nhớ ảnh gốc trước lần cắt đầu tiên + vị trí của ảnh gốc theo toạ độ
    // ảnh mới. Nới khung ra sau khi cắt → phần mới hiện lại nội dung gốc thay vì nền trắng. Gắn với chính
    // đối tượng bitmap nên Undo/Redo (chỉ đổi qua lại các bitmap) tự đúng, không cần command riêng. Ảnh sinh
    // từ thao tác pixel khác (Tô màu, Xoá vùng, Flatten, Dán) kế thừa của ảnh trước (xem OnBitmapChanged).
    // Lưu qua phiên làm việc (SessionCropSource): mở lại app vẫn kéo khung ra lấy lại được phần đã cắt.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SKBitmap, CropSource> CropSources = new();

    private sealed record CropSource(SKBitmap Original, SKPointI Offset);

    partial void OnBitmapChanged(SKBitmap? oldValue, SKBitmap newValue)
    {
        if (oldValue is not null && !CropSources.TryGetValue(newValue, out _)
            && CropSources.TryGetValue(oldValue, out var inherited))
        {
            CropSources.AddOrUpdate(newValue, inherited);
        }
    }

    /// <summary>Ảnh gốc của <see cref="Bitmap"/> và vị trí của nó khi khung ảnh dịch góc trên-trái tới
    /// <paramref name="newTopLeft"/> (toạ độ ảnh hiện tại).</summary>
    private CropSource SourceAfterReframe(SKPointI newTopLeft)
    {
        var current = CropSources.TryGetValue(Bitmap, out var source) ? source : new CropSource(Bitmap, SKPointI.Empty);
        return current with { Offset = new SKPointI(current.Offset.X - newTopLeft.X, current.Offset.Y - newTopLeft.Y) };
    }

    public void Crop(SKRect cropRect)
    {
        var info = new SKImageInfo((int)cropRect.Width, (int)cropRect.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var cropped = new SKBitmap(info);
        using (var canvas = new SKCanvas(cropped))
        {
            canvas.DrawBitmap(Bitmap, cropRect, new SKRect(0, 0, cropRect.Width, cropRect.Height));
        }
        CropSources.AddOrUpdate(cropped, SourceAfterReframe(new SKPointI((int)cropRect.Left, (int)cropRect.Top)));

        UndoRedo.Do(new CropCommand(newBitmap => Bitmap = newBitmap, Annotations, Bitmap, cropped, cropRect));
    }

    /// <summary>Đổi khung ảnh thành <paramref name="newRect"/> (toạ độ theo ảnh hiện tại, có thể âm hoặc
    /// vượt kích thước ảnh). Phần mới mở rộng: nội dung gốc đã bị Cắt trước đó (nếu có), ngoài ra tô trắng
    /// như PicPick.</summary>
    public void ResizeCanvas(SKRectI newRect)
    {
        if (newRect.Width < 1 || newRect.Height < 1)
        {
            return;
        }
        var info = new SKImageInfo(newRect.Width, newRect.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var resized = new SKBitmap(info);
        var source = SourceAfterReframe(new SKPointI(newRect.Left, newRect.Top));
        using (var canvas = new SKCanvas(resized))
        {
            canvas.Clear(BackgroundFill);
            if (!ReferenceEquals(source.Original, Bitmap))
            {
                canvas.DrawBitmap(source.Original, source.Offset.X, source.Offset.Y);
            }
            canvas.DrawBitmap(Bitmap, -newRect.Left, -newRect.Top);
        }
        CropSources.AddOrUpdate(resized, source);
        UndoRedo.Do(new ResizeCanvasCommand(newBitmap => Bitmap = newBitmap, Annotations, Bitmap, resized,
            -newRect.Left, -newRect.Top));
    }

    /// <summary>Xoay / lật cả ảnh (cùng mọi shape) - 1 bước Undo.</summary>
    public void Transform(ImageTransformKind kind) =>
        ApplyTransform(ImageTransform.Create(kind, Bitmap.Width, Bitmap.Height));

    /// <summary>Hướng ảnh so với lúc chụp / mở - đổi theo Xoay / Lật (cả Undo / Redo), lưu qua phiên.</summary>
    [ObservableProperty]
    private ImageOrientation _orientation;

    /// <summary>Xoay / lật ngược mọi lần Xoay / Lật trước đó (tối đa 2 bước, gộp 1 bước Undo); hình vẽ đi theo ảnh như khi
    /// xoay / lật thường. Cắt / đổi khung / đổi cỡ ảnh giữa chừng vẫn giữ.</summary>
    public void ResetOrientation()
    {
        var steps = Orientation.UndoSteps();
        if (steps.Count == 0)
        {
            StatusText = "Ảnh đang ở hướng ban đầu.";
            return;
        }
        // Mỗi bước tính trên kết quả bước trước → chạy thử lần lượt rồi trả lại, sau đó Do cả nhóm (Execute lại chỉ gán
        // ảnh / danh sách shape đã tính sẵn).
        var commands = new List<IEditCommand>();
        foreach (var kind in steps)
        {
            var command = CreateTransformCommand(ImageTransform.Create(kind, Bitmap.Width, Bitmap.Height));
            command.Execute();
            commands.Add(command);
        }
        for (int i = commands.Count - 1; i >= 0; i--)
        {
            commands[i].Undo();
        }
        SelectedAnnotation = null;
        UndoRedo.Do(new CompositeEditCommand(commands, "Về hướng ban đầu"));
        StatusText = $"Về hướng ban đầu: ảnh giờ là {Bitmap.Width} × {Bitmap.Height} px.";
    }

    /// <summary>Co giãn cả ảnh (cùng mọi shape) về <paramref name="width"/> × <paramref name="height"/> px.</summary>
    public void ResizeImage(int width, int height)
    {
        if (width != Bitmap.Width || height != Bitmap.Height)
        {
            ApplyTransform(ImageTransform.CreateResize(Bitmap.Width, Bitmap.Height, width, height));
        }
    }

    private void ApplyTransform(ImageTransform transform)
    {
        var command = CreateTransformCommand(transform);
        SelectedAnnotation = null;
        UndoRedo.Do(command);
        StatusText = $"{transform.Description}: ảnh giờ là {Bitmap.Width} × {Bitmap.Height} px.";
    }

    private TransformImageCommand CreateTransformCommand(ImageTransform transform)
    {
        var command = new TransformImageCommand(newBitmap => Bitmap = newBitmap, Annotations, Bitmap, transform,
            orientation => Orientation = orientation, Orientation);
        // Phần đã Cắt trước đó không còn khớp hướng / tỉ lệ ảnh mới → ảnh mới là gốc của chính nó (không kế thừa
        // nguồn cắt của ảnh cũ trong OnBitmapChanged). Undo trả lại ảnh cũ thì nguồn cắt cũ vẫn còn.
        CropSources.AddOrUpdate(command.NewBitmap, new CropSource(command.NewBitmap, SKPointI.Empty));
        return command;
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
                canvas.Clear(BackgroundFill);
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
        UndoRedo.Do(new EraseRegionCommand(newBitmap => Bitmap = newBitmap, Bitmap, region, BackgroundFill));

    /// <summary>Màu tô phần ảnh mới sinh ra (nới khung, xoá vùng, dán làm ảnh to ra): trắng như PicPick, riêng ảnh có nền
    /// trong suốt (Ảnh mới "Trong suốt", đã đổ bóng / xé mép) thì tô trong suốt.</summary>
    private SKColor BackgroundFill => ImageEffects.HasTransparency(Bitmap) ? SKColors.Transparent : SKColors.White;

    /// <summary>Áp 1 hiệu ứng ảnh (xem <see cref="ImageEffects"/>) lên ảnh nền - 1 bước Undo. Các hình đã vẽ giữ nguyên
    /// (không bị hiệu ứng), dời theo <see cref="EffectResult.Offset"/> khi ảnh nới ra (viền, bóng).</summary>
    public void ApplyEffect(EffectResult result, string description)
    {
        // Phần đã Cắt trước đó không còn khớp ảnh đã áp hiệu ứng → ảnh mới là gốc của chính nó (như xoay / đổi cỡ ảnh).
        CropSources.AddOrUpdate(result.Bitmap, new CropSource(result.Bitmap, SKPointI.Empty));
        SelectedAnnotation = null;
        UndoRedo.Do(new ResizeCanvasCommand(newBitmap => Bitmap = newBitmap, Annotations, Bitmap, result.Bitmap,
            result.Offset.X, result.Offset.Y, description));
        StatusText = $"{description}: ảnh giờ là {Bitmap.Width} × {Bitmap.Height} px.";
    }

    public void FloodFill(SKPointI seed) =>
        UndoRedo.Do(new FloodFillCommand(newBitmap => Bitmap = newBitmap, Bitmap, seed, StrokeColor));

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => UndoRedo.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => UndoRedo.Redo();

    /// <summary>Tên tab của ảnh trong Editor (thời điểm chụp, kiểu PicPick "2026-09-24 13 36 14").</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Mức zoom hiển thị của tab (1 = 100%). Chỉ là trạng thái xem - không ảnh hưởng ảnh khi
    /// lưu/copy, không ghi vào phiên làm việc.</summary>
    public float Zoom { get; set; } = 1f;

    /// <summary>Định danh ổn định của tab - tên file lưu tạm phiên làm việc (SessionService).</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    private bool _savedToFile;
    public bool SavedToFile => _savedToFile;

    /// <summary>Nạp lại shape + trạng thái đã lưu của 1 tab từ phiên trước. Không đi qua Undo (mở lại
    /// app không có lịch sử Undo), shape vẫn chỉnh sửa được như bình thường.</summary>
    /// <summary>Ảnh gốc trước khi cắt của ảnh hiện tại, để ghi vào phiên làm việc; null nếu chưa cắt / đổi khung.</summary>
    public SessionCropSource? CropSourceForSession =>
        CropSources.TryGetValue(Bitmap, out var source) && !ReferenceEquals(source.Original, Bitmap)
            ? new SessionCropSource(source.Original, source.Offset.X, source.Offset.Y)
            : null;

    /// <param name="filePath">File gắn với tab (xem <see cref="FilePath"/>), null nếu chưa có.</param>
    public void RestoreFromSession(IEnumerable<AnnotationShape> shapes, bool savedToFile, SessionCropSource? crop = null, string? filePath = null,
        ImageOrientation orientation = default)
    {
        FilePath = filePath;
        Orientation = orientation;
        if (crop is not null)
        {
            CropSources.AddOrUpdate(Bitmap, new CropSource(crop.Original, new SKPointI(crop.OffsetX, crop.OffsetY)));
        }
        foreach (var shape in shapes)
        {
            Annotations.Add(shape);
        }
        _savedToFile = savedToFile;
    }

    /// <summary>Ảnh chưa từng lưu ra file, đã sửa sau lần lưu gần nhất, hoặc file đã lưu không còn (bị xoá / đổi tên
    /// ngoài app - vd xoá ảnh tự lưu rồi mới đóng tab) → đóng tab/cửa sổ phải hỏi, tránh mất ảnh chụp (kể cả ảnh vừa chụp
    /// chưa sửa gì).</summary>
    public bool NeedsSave => !_savedToFile || UndoRedo.IsDirty || FileMissing;

    /// <summary>Tab đã gắn 1 file nhưng file đó không còn trên ổ.</summary>
    public bool FileMissing => FilePath is { } path && !File.Exists(path);

    /// <summary>File gắn với tab: lần lưu gần nhất (Lưu / Lưu thành / tự lưu / Lưu tất cả) hoặc file đã mở. Null = ảnh chụp
    /// chưa lưu. Lưu (Ctrl+S) ghi thẳng vào file này nếu định dạng ghi được (PNG / JPG / BMP).</summary>
    public string? FilePath { get; private set; }

    /// <summary>Vừa lưu ra file (Lưu / Lưu thành / tự lưu / Lưu tất cả) - Editor ghi lại phiên ngay để tắt app đột ngột
    /// cũng không quên tab đã lưu vào file nào.</summary>
    public event EventHandler? Saved;

    [RelayCommand]
    private async Task SaveAsync() => await SaveToFileAsync();

    [RelayCommand]
    private async Task SaveAsAsync() => await SaveAsToFileAsync();

    /// <summary>Ghi PNG (ảnh + shape) vào thư mục đã chọn sẵn, tên file = tên tab. Dùng cho "Lưu tất cả" / tự lưu.</summary>
    /// <param name="relativeName">Tên file (chưa có đuôi), có thể kèm thư mục con - vd theo mẫu tên khi tự lưu
    /// (<see cref="FileNameTemplate"/>); null = tên tab.</param>
    public string SaveToFolder(string folder, string? relativeName = null)
    {
        if (relativeName is not null && Path.GetDirectoryName(relativeName) is { Length: > 0 } subfolder)
        {
            folder = Path.Combine(folder, subfolder);
            Directory.CreateDirectory(folder);
            relativeName = Path.GetFileName(relativeName);
        }
        var path = _fileService.SavePngToFolder(RenderComposited(), folder, relativeName ?? Title);
        MarkSaved(path);
        return path;
    }

    /// <summary>Lưu: tab đã gắn file ghi được → ghi đè file đó (đúng định dạng của nó), không hỏi; chưa có (ảnh chụp mới,
    /// hoặc mở từ GIF / WEBP) → Lưu thành. False nếu huỷ hoặc lưu lỗi.</summary>
    public async Task<bool> SaveToFileAsync() =>
        FilePath is { } path && ImageFileService.CanSaveAs(path) ? await WriteAsync(path) : await SaveAsToFileAsync();

    /// <summary>Lưu thành: chọn đường dẫn + định dạng (PNG / JPG / BMP; chọn sẵn định dạng của file hiện tại, chưa có thì
    /// PNG), tên điền sẵn = tên file hiện tại hoặc tên tab. False nếu huỷ hoặc lưu lỗi.</summary>
    public async Task<bool> SaveAsToFileAsync()
    {
        var name = FilePath is { } current ? Path.GetFileNameWithoutExtension(current) : Title;
        var ext = FilePath is { } p && ImageFileService.CanSaveAs(p) ? Path.GetExtension(p) : ".png";
        var path = await _fileService.PickSavePathAsync(_ownerHwnd, name, ext);
        if (path is null)
        {
            StatusText = "Đã huỷ lưu.";
            return false;
        }
        return await WriteAsync(path);
    }

    private async Task<bool> WriteAsync(string path)
    {
        var composited = RenderComposited();
        try
        {
            // Lưu lại vào file đã bị xoá cùng thư mục (vd thư mục ngày của mẫu {date}\...) → tạo lại thư mục.
            if (Path.GetDirectoryName(path) is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
            }
            await Task.Run(() => _fileService.WriteImage(composited, path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // File đang bị app khác giữ / không có quyền ghi / đầy ổ: báo, không đánh dấu đã lưu, không văng app.
            StatusText = $"Không lưu được {Path.GetFileName(path)}: {ex.Message}";
            return false;
        }
        finally
        {
            composited.Dispose();
        }
        MarkSaved(path);
        return true;
    }

    private void MarkSaved(string path)
    {
        FilePath = path;
        _savedToFile = true;
        UndoRedo.MarkClean();
        StatusText = $"Đã lưu: {path}";
        Saved?.Invoke(this, EventArgs.Empty);
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
            shape.Draw(canvas, Bitmap);
        }
        return result;
    }
}
