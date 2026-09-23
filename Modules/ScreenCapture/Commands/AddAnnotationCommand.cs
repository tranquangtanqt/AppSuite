using System.Collections.ObjectModel;
using ScreenCapture.Models;

namespace ScreenCapture.Commands;

public sealed class AddAnnotationCommand : IEditCommand
{
    private readonly ObservableCollection<AnnotationShape> _annotations;
    private readonly AnnotationShape _shape;

    public AddAnnotationCommand(ObservableCollection<AnnotationShape> annotations, AnnotationShape shape)
    {
        _annotations = annotations;
        _shape = shape;
    }

    public string Description => $"Thêm {_shape.DisplayName}";
    public bool ChangesStructure => true;

    public void Execute() => _annotations.Add(_shape);
    public void Undo() => _annotations.Remove(_shape);
}
