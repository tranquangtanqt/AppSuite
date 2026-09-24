using ScreenCapture.Models;
using SkiaSharp;

namespace ScreenCapture.Commands;

/// <summary>Đổi Fill (Color kế thừa) và/hoặc Outline của 1 StampAnnotation - dùng chung cho "Shape
/// Styles" (preset màu, chỉ đổi Fill) và "Shape Colors" (ColorPicker Outline/Fill riêng) ở tab
/// contextual "Number Stamp".</summary>
public sealed class ChangeStampColorsCommand : IEditCommand
{
    private readonly StampAnnotation _shape;
    private readonly SKColor _oldFill;
    private readonly SKColor _newFill;
    private readonly SKColor _oldOutline;
    private readonly SKColor _newOutline;

    public ChangeStampColorsCommand(StampAnnotation shape, SKColor oldFill, SKColor newFill,
        SKColor oldOutline, SKColor newOutline)
    {
        _shape = shape;
        _oldFill = oldFill;
        _newFill = newFill;
        _oldOutline = oldOutline;
        _newOutline = newOutline;
    }

    public string Description => "Đổi màu stamp";
    public bool ChangesStructure => false;

    public void Execute()
    {
        _shape.Color = _newFill;
        _shape.OutlineColor = _newOutline;
    }

    public void Undo()
    {
        _shape.Color = _oldFill;
        _shape.OutlineColor = _oldOutline;
    }
}
