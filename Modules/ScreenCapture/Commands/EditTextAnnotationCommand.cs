using ScreenCapture.Models;

namespace ScreenCapture.Commands;

public sealed class EditTextAnnotationCommand : IEditCommand
{
    private readonly TextAnnotation _shape;
    private readonly string _oldText;
    private readonly string _newText;

    public EditTextAnnotationCommand(TextAnnotation shape, string oldText, string newText)
    {
        _shape = shape;
        _oldText = oldText;
        _newText = newText;
    }

    public string Description => "Sửa text";
    public bool ChangesStructure => false;

    public void Execute() => _shape.Text = _newText;
    public void Undo() => _shape.Text = _oldText;
}
