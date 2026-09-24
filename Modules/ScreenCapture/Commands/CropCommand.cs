using System.Collections.ObjectModel;
using ScreenCapture.Models;
using SkiaSharp;

namespace ScreenCapture.Commands;

/// <summary>Destructive crop: replaces the bitmap outright and drops annotations that fall fully
/// outside the new bounds. Simpler than a non-destructive, re-adjustable crop rect - still fully
/// undoable, which is enough for Phase 1 (see PLAN.md "Chưa làm").</summary>
public sealed class CropCommand : IEditCommand
{
    private readonly Action<SKBitmap> _setBitmap;
    private readonly ObservableCollection<AnnotationShape> _annotations;
    private readonly SKBitmap _oldBitmap;
    private readonly SKBitmap _newBitmap;
    private readonly List<AnnotationShape> _oldAnnotations;
    private readonly List<AnnotationShape> _keptAnnotations;

    public CropCommand(Action<SKBitmap> setBitmap, ObservableCollection<AnnotationShape> annotations,
        SKBitmap oldBitmap, SKBitmap newBitmap, SKRect cropRect)
    {
        _setBitmap = setBitmap;
        _annotations = annotations;
        _oldBitmap = oldBitmap;
        _newBitmap = newBitmap;
        _oldAnnotations = [.. annotations];
        // Inflate 1px: đường thẳng ngang/dọc có khung cao/rộng 0, Intersect sẽ ra Empty và bị xoá nhầm.
        _keptAnnotations = _oldAnnotations.Where(a => SKRect.Intersect(SKRect.Inflate(a.NormalizedBounds, 1, 1), cropRect) != SKRect.Empty).ToList();
    }

    public string Description => "Cắt ảnh";
    public bool ChangesStructure => true;

    public void Execute()
    {
        _setBitmap(_newBitmap);
        _annotations.Clear();
        foreach (var shape in _keptAnnotations)
        {
            _annotations.Add(shape);
        }
    }

    public void Undo()
    {
        _setBitmap(_oldBitmap);
        _annotations.Clear();
        foreach (var shape in _oldAnnotations)
        {
            _annotations.Add(shape);
        }
    }
}
