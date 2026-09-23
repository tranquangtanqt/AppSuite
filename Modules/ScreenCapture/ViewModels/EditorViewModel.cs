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
