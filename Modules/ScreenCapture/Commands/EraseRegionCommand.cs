using SkiaSharp;

namespace ScreenCapture.Commands;

/// <summary>Xoá (tô trắng) 1 vùng chữ nhật trên ảnh nền - dùng cho "Xoá vùng" và "Cut" của tool
/// Select. Chỉ đổi pixel ảnh nền, không đụng tới annotation (giống FloodFillCommand).</summary>
public sealed class EraseRegionCommand : IEditCommand
{
    private readonly Action<SKBitmap> _setBitmap;
    private readonly SKBitmap _oldBitmap;
    private readonly SKBitmap _newBitmap;

    public EraseRegionCommand(Action<SKBitmap> setBitmap, SKBitmap oldBitmap, SKRectI region)
    {
        _setBitmap = setBitmap;
        _oldBitmap = oldBitmap;
        _newBitmap = oldBitmap.Copy();
        using var canvas = new SKCanvas(_newBitmap);
        using var paint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        canvas.DrawRect(SKRect.Create(region.Left, region.Top, region.Width, region.Height), paint);
    }

    public string Description => "Xoá vùng chọn";
    public bool ChangesStructure => false;

    public void Execute() => _setBitmap(_newBitmap);
    public void Undo() => _setBitmap(_oldBitmap);
}
