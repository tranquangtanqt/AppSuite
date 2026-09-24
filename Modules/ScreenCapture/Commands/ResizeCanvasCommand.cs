using System.Collections.ObjectModel;
using ScreenCapture.Models;
using SkiaSharp;

namespace ScreenCapture.Commands;

/// <summary>Đổi kích thước khung ảnh (kéo 8 handle quanh ảnh bằng tool Move, giống PicPick): thay bitmap
/// bằng bitmap mới (phần mở rộng tô nền trắng, phần thu lại bị cắt) và dịch mọi annotation theo
/// (dx, dy) để chúng giữ nguyên vị trí so với nội dung ảnh khi khung mở rộng/cắt ở cạnh trái/trên.</summary>
public sealed class ResizeCanvasCommand : IEditCommand
{
    private readonly Action<SKBitmap> _setBitmap;
    private readonly ObservableCollection<AnnotationShape> _annotations;
    private readonly SKBitmap _oldBitmap;
    private readonly SKBitmap _newBitmap;
    private readonly float _dx;
    private readonly float _dy;

    public ResizeCanvasCommand(Action<SKBitmap> setBitmap, ObservableCollection<AnnotationShape> annotations,
        SKBitmap oldBitmap, SKBitmap newBitmap, float dx, float dy)
    {
        _setBitmap = setBitmap;
        _annotations = annotations;
        _oldBitmap = oldBitmap;
        _newBitmap = newBitmap;
        _dx = dx;
        _dy = dy;
    }

    public string Description => "Đổi kích thước ảnh";
    public bool ChangesStructure => true;

    public void Execute()
    {
        _setBitmap(_newBitmap);
        OffsetAll(_annotations, _dx, _dy);
    }

    public void Undo()
    {
        _setBitmap(_oldBitmap);
        OffsetAll(_annotations, -_dx, -_dy);
    }

    /// <summary>Dịch từng cạnh thay vì dùng SKRect.Offset/Standardized - Bounds của Line/Arrow lưu
    /// điểm đầu/cuối, không được chuẩn hoá (xem LineArrowAnnotation).</summary>
    internal static void OffsetAll(IEnumerable<AnnotationShape> shapes, float dx, float dy)
    {
        foreach (var shape in shapes)
        {
            var b = shape.Bounds;
            shape.Bounds = new SKRect(b.Left + dx, b.Top + dy, b.Right + dx, b.Bottom + dy);
        }
    }
}
