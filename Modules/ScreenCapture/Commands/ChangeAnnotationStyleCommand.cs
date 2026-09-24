using ScreenCapture.Models;
using SkiaSharp;

namespace ScreenCapture.Commands;

/// <summary>Đổi màu/độ dày nét của 1 shape ĐÃ đặt (khác với việc đổi Color1/Color2/Size chỉ ảnh
/// hưởng shape vẽ tiếp theo) - dùng khi có SelectedAnnotation (Move tool).</summary>
public sealed class ChangeAnnotationStyleCommand : IEditCommand
{
    private readonly AnnotationShape _shape;
    private readonly SKColor _oldColor;
    private readonly SKColor _newColor;
    private readonly float _oldStrokeWidth;
    private readonly float _newStrokeWidth;

    public ChangeAnnotationStyleCommand(AnnotationShape shape, SKColor oldColor, SKColor newColor,
        float oldStrokeWidth, float newStrokeWidth)
    {
        _shape = shape;
        _oldColor = oldColor;
        _newColor = newColor;
        _oldStrokeWidth = oldStrokeWidth;
        _newStrokeWidth = newStrokeWidth;
    }

    public string Description => "Đổi màu/cỡ nét";
    public bool ChangesStructure => false;

    public void Execute()
    {
        _shape.Color = _newColor;
        _shape.StrokeWidth = _newStrokeWidth;
    }

    public void Undo()
    {
        _shape.Color = _oldColor;
        _shape.StrokeWidth = _oldStrokeWidth;
    }
}
