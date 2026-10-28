namespace ScreenCapture.Models;

/// <summary>Which annotation tool is currently active in the editor toolbar.</summary>
public enum CaptureTool
{
    None,
    Move,
    /// <summary>Kéo chọn 1 vùng chữ nhật trên ảnh để Cắt ảnh / Copy / Cut / Xoá vùng.</summary>
    Select,
    Rectangle,
    /// <summary>Chữ nhật tô nền trong mờ (viền Color1, nền Color2 ~37%) - như "Highlight Rectangle" của PicPick.
    /// Vẽ ra <see cref="RectangleAnnotation"/> có FillColor, sửa như chữ nhật thường.</summary>
    FilledRectangle,
    Ellipse,
    Line,
    Arrow,
    Highlight,
    /// <summary>Bút vẽ tự do (FreehandAnnotation).</summary>
    Pen,
    Text,
    /// <summary>Khung chú thích / bong bóng lời nói (CalloutAnnotation).</summary>
    Callout,
    Fill,
    Stamp,
    /// <summary>Che vùng bằng ô vuông pixel / làm mờ (RedactAnnotation).</summary>
    Mosaic,
    Blur,
}
