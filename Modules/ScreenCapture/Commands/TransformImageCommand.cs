using System.Collections.ObjectModel;
using ScreenCapture.Models;
using SkiaSharp;

namespace ScreenCapture.Commands;

/// <summary>Xoay / lật / co giãn cả ảnh (xem <see cref="ImageTransform"/>): thay ảnh nền và thay cả danh sách shape bằng
/// bản sao đã biến đổi. Không sửa shape cũ tại chỗ → Undo chỉ cần đặt lại ảnh + danh sách cũ, và các bước Undo trước
/// đó (di chuyển, đổi màu... trên chính các shape cũ) vẫn đúng.</summary>
public sealed class TransformImageCommand : IEditCommand
{
    private readonly Action<SKBitmap> _setBitmap;
    private readonly ObservableCollection<AnnotationShape> _annotations;
    private readonly SKBitmap _oldBitmap;
    private readonly SKBitmap _newBitmap;
    private readonly List<AnnotationShape> _oldAnnotations;
    private readonly List<AnnotationShape> _newAnnotations;
    private readonly Action<ImageOrientation> _setOrientation;
    private readonly ImageOrientation _oldOrientation;

    public TransformImageCommand(Action<SKBitmap> setBitmap, ObservableCollection<AnnotationShape> annotations,
        SKBitmap oldBitmap, ImageTransform transform, Action<ImageOrientation> setOrientation, ImageOrientation oldOrientation)
    {
        _setBitmap = setBitmap;
        _annotations = annotations;
        _oldBitmap = oldBitmap;
        _newBitmap = transform.Apply(oldBitmap);
        _oldAnnotations = [.. annotations];
        _newAnnotations = _oldAnnotations.Select(a => transform.Transform(a) ?? a).ToList();
        _setOrientation = setOrientation;
        _oldOrientation = oldOrientation;
        NewOrientation = oldOrientation.Then(transform.Kind);
        Description = transform.Description;
    }

    /// <summary>Hướng ảnh sau biến đổi (xem <see cref="ImageOrientation"/>).</summary>
    public ImageOrientation NewOrientation { get; }

    public string Description { get; }
    public bool ChangesStructure => true;

    /// <summary>Ảnh nền sau biến đổi - bên gọi cần để đăng ký lại nguồn "Cắt ảnh khôi phục được" (xem EditorViewModel).</summary>
    public SKBitmap NewBitmap => _newBitmap;

    public void Execute()
    {
        Apply(_newBitmap, _newAnnotations);
        _setOrientation(NewOrientation);
    }

    public void Undo()
    {
        Apply(_oldBitmap, _oldAnnotations);
        _setOrientation(_oldOrientation);
    }

    private void Apply(SKBitmap bitmap, List<AnnotationShape> shapes)
    {
        _setBitmap(bitmap);
        _annotations.Clear();
        foreach (var shape in shapes)
        {
            _annotations.Add(shape);
        }
    }
}
