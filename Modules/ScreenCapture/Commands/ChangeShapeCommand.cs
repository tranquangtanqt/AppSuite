using ScreenCapture.Models;

namespace ScreenCapture.Commands;

/// <summary>Đổi 1 shape ĐÃ đặt từ trạng thái <c>before</c> sang <c>after</c> (bản chụp <see cref="AnnotationShape.Snapshot"/>):
/// di chuyển / co giãn, đổi màu / nét / phông, sửa chữ, kéo đuôi khung chú thích... - mọi thay đổi khung + thuộc tính đi
/// chung 1 lệnh nên đổi phông (khung chữ đổi cỡ theo) vẫn là 1 bước Undo.</summary>
public sealed class ChangeShapeCommand : IEditCommand
{
    private readonly AnnotationShape _shape;
    private readonly AnnotationShape _before;
    private readonly AnnotationShape _after;

    public ChangeShapeCommand(AnnotationShape shape, AnnotationShape before, AnnotationShape after, string description)
    {
        _shape = shape;
        _before = before;
        _after = after;
        Description = description;
    }

    public string Description { get; }
    public bool ChangesStructure => false;

    public void Execute() => _shape.RestoreFrom(_after);
    public void Undo() => _shape.RestoreFrom(_before);
}
