using ScreenCapture.Models;

namespace ScreenCapture.Commands;

/// <summary>Sửa số hiển thị (NumberValue) của 1 Number Stamp đã đặt - ô "Current" ở tab contextual
/// "Number Stamp" (giống PicPick).</summary>
public sealed class ChangeStampNumberCommand : IEditCommand
{
    private readonly StampAnnotation _shape;
    private readonly int _oldValue;
    private readonly int _newValue;

    public ChangeStampNumberCommand(StampAnnotation shape, int oldValue, int newValue)
    {
        _shape = shape;
        _oldValue = oldValue;
        _newValue = newValue;
    }

    public string Description => "Đổi số stamp";
    public bool ChangesStructure => false;

    public void Execute() => _shape.NumberValue = _newValue;
    public void Undo() => _shape.NumberValue = _oldValue;
}
