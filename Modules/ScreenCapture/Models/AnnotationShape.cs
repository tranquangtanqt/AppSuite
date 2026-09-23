using SkiaSharp;

namespace ScreenCapture.Models;

/// <summary>One drawn annotation on top of the captured bitmap. Subclasses own their own SkiaSharp
/// draw call, keeping EditorWindow/EditorViewModel shape-agnostic.</summary>
public abstract class AnnotationShape
{
    public SKRect Bounds { get; set; }
    public SKColor Color { get; set; } = SKColors.Red;

    public abstract string DisplayName { get; }
    public abstract void Render(SKCanvas canvas);
}
