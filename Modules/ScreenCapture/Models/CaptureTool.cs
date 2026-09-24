namespace ScreenCapture.Models;

/// <summary>Which annotation tool is currently active in the editor toolbar.</summary>
public enum CaptureTool
{
    None,
    Move,
    /// <summary>Kéo chọn 1 vùng chữ nhật trên ảnh để Cắt ảnh / Copy / Cut / Xoá vùng.</summary>
    Select,
    Rectangle,
    Ellipse,
    Line,
    Arrow,
    Highlight,
    Text,
    Fill,
    Stamp,
}
