using ScreenCapture.Models;
using SkiaSharp;

namespace ScreenCapture.Commands;

public sealed class MoveResizeAnnotationCommand : IEditCommand
{
    private readonly AnnotationShape _shape;
    private readonly SKRect _oldBounds;
    private readonly SKRect _newBounds;

    public MoveResizeAnnotationCommand(AnnotationShape shape, SKRect oldBounds, SKRect newBounds)
    {
        _shape = shape;
        _oldBounds = oldBounds;
        _newBounds = newBounds;
    }

    public string Description => $"Di chuyển {_shape.DisplayName}";
    public bool ChangesStructure => false;

    public void Execute() => _shape.Bounds = _newBounds;
    public void Undo() => _shape.Bounds = _oldBounds;
}
