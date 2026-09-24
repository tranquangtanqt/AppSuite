using System.Collections.ObjectModel;
using ScreenCapture.Models;

namespace ScreenCapture.Commands;

/// <summary>Bring to Front / Send to Back - thay đổi thứ tự vẽ (item cuối trong Annotations nằm
/// trên cùng, xem EditorWindow.Canvas_PaintSurface).</summary>
public sealed class ReorderAnnotationCommand : IEditCommand
{
    private readonly ObservableCollection<AnnotationShape> _annotations;
    private readonly int _oldIndex;
    private readonly int _newIndex;

    public ReorderAnnotationCommand(ObservableCollection<AnnotationShape> annotations, int oldIndex, int newIndex)
    {
        _annotations = annotations;
        _oldIndex = oldIndex;
        _newIndex = newIndex;
    }

    public string Description => _newIndex > _oldIndex ? "Đưa lên trên" : "Đẩy xuống dưới";
    public bool ChangesStructure => true;

    public void Execute() => _annotations.Move(_oldIndex, _newIndex);
    public void Undo() => _annotations.Move(_newIndex, _oldIndex);
}
