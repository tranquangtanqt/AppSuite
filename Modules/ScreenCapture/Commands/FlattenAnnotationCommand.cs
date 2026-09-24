using System.Collections.ObjectModel;
using ScreenCapture.Models;
using SkiaSharp;

namespace ScreenCapture.Commands;

/// <summary>Nướng (rasterize) 1 annotation vào bitmap nền vĩnh viễn rồi bỏ nó khỏi danh sách vector -
/// giống nút "Flatten" của PicPick. Cấu trúc giống CropCommand (thay bitmap, có bitmap cũ để undo),
/// khác ở chỗ chỉ 1 shape bị gộp thay vì crop toàn ảnh.</summary>
public sealed class FlattenAnnotationCommand : IEditCommand
{
    private readonly Action<SKBitmap> _setBitmap;
    private readonly ObservableCollection<AnnotationShape> _annotations;
    private readonly SKBitmap _oldBitmap;
    private readonly SKBitmap _newBitmap;
    private readonly AnnotationShape _shape;
    private readonly int _index;

    public FlattenAnnotationCommand(Action<SKBitmap> setBitmap, ObservableCollection<AnnotationShape> annotations,
        SKBitmap oldBitmap, AnnotationShape shape)
    {
        _setBitmap = setBitmap;
        _annotations = annotations;
        _oldBitmap = oldBitmap;
        _shape = shape;
        _index = annotations.IndexOf(shape);

        var info = new SKImageInfo(oldBitmap.Width, oldBitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        _newBitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(_newBitmap);
        canvas.DrawBitmap(oldBitmap, 0, 0);
        shape.Render(canvas);
    }

    public string Description => "Flatten stamp";
    public bool ChangesStructure => true;

    public void Execute()
    {
        _setBitmap(_newBitmap);
        _annotations.Remove(_shape);
    }

    public void Undo()
    {
        _setBitmap(_oldBitmap);
        _annotations.Insert(_index, _shape);
    }
}
