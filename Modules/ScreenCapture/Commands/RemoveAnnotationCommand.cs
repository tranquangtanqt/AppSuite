using System.Collections.ObjectModel;
using ScreenCapture.Models;

namespace ScreenCapture.Commands;

public sealed class RemoveAnnotationCommand : IEditCommand
{
    private readonly ObservableCollection<AnnotationShape> _annotations;
    private readonly AnnotationShape _shape;
    private int _index;

    public RemoveAnnotationCommand(ObservableCollection<AnnotationShape> annotations, AnnotationShape shape)
    {
        _annotations = annotations;
        _shape = shape;
    }

    public string Description => $"Xoá {_shape.DisplayName}";
    public bool ChangesStructure => true;

    public void Execute()
    {
        _index = _annotations.IndexOf(_shape);
        _annotations.Remove(_shape);
    }

    public void Undo() => _annotations.Insert(_index, _shape);
}
