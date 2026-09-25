using System.Diagnostics;
using SkiaSharp;

namespace ImageCompare.Engine;

/// <summary>Kết quả so sánh căn theo dòng, vẽ thành 1 "ảnh ghép" dọc theo thứ tự các dải (<see cref="RowAligner"/>):
/// dải ghép cặp = ảnh B nhạt màu + pixel khác tô đỏ; dải chỉ có ở B (thêm vào) = nội dung B phủ cam; dải chỉ có
/// ở A (bị bỏ) = nội dung A phủ xanh. Toạ độ ngang theo ảnh A (B lệch dx), toạ độ dọc theo ảnh ghép.</summary>
public sealed class RowDiffView : IDiffView
{
    private readonly SKBitmap _a, _b;
    private readonly int _dx;
    private readonly List<Band> _bands;
    private readonly List<DiffRegion> _regions;

    /// <summary>1 dải trên ảnh ghép: vị trí dọc <see cref="OutY"/>, mặt nạ khác biệt (dải ghép cặp có khác).</summary>
    private sealed class Band
    {
        public required RowBand Rows { get; init; }
        public required int OutY { get; init; }
        public SKRectI Overlap { get; init; } // theo toạ độ ảnh A (dải ghép cặp)
        public byte[]? Mask { get; init; }
        public SKBitmap? Overlay; // field (không phải property) để dùng được với LazyInitializer (ref)
    }

    private RowDiffView(SKBitmap a, SKBitmap b, int dx, List<Band> bands, List<DiffRegion> regions, DiffStats stats)
    {
        _a = a;
        _b = b;
        _dx = dx;
        _bands = bands;
        _regions = regions;
        Stats = stats;
    }

    public DiffStats Stats { get; }
    public IReadOnlyList<DiffRegion> Regions => _regions;
    public SKRectI Bounds => new(Math.Min(0, _dx), 0, Math.Max(_a.Width, _b.Width + _dx),
        _bands.Count == 0 ? 0 : _bands[^1].OutY + _bands[^1].Rows.Height);

    /// <summary>So sánh căn theo dòng; null nếu 2 ảnh khác nhau quá nhiều (gọi nơi khác lui về tự căn dịch chuyển).</summary>
    public static RowDiffView? Create(SKBitmap a, SKBitmap b, DiffOptions options, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        // Độ lệch ngang: lấy từ căn dịch chuyển (hồ sơ cột không bị ảnh hưởng bởi đoạn thêm / bớt theo chiều dọc).
        int dx = Aligner.FindOffset(a, b, ct).X;
        var rowBands = RowAligner.Align(a, b, dx, ct);
        if (rowBands is null)
        {
            return null;
        }

        var bands = new List<Band>();
        var found = new List<(SKRectI Bounds, int Pixels, RegionKind Kind)>();
        int x0 = Math.Max(0, dx), x1 = Math.Min(a.Width, b.Width + dx);
        int outY = 0;
        long diffPixels = 0, compared = 0, onlyA = 0, onlyB = 0;
        foreach (var rows in rowBands)
        {
            if (rows.IsPaired)
            {
                var overlap = new SKRectI(x0, rows.AY, x1, rows.AY + rows.Height);
                var offset = new SKPointI(dx, rows.AY - rows.BY);
                var (mask, pixels, regions) = PixelDiff.Compare(a, b, offset, overlap, options, ct);
                bands.Add(new Band { Rows = rows, OutY = outY, Overlap = overlap, Mask = pixels > 0 ? mask : null });
                diffPixels += pixels;
                compared += (long)overlap.Width * overlap.Height;
                int shift = outY - rows.AY;
                found.AddRange(regions.Select(r => (new SKRectI(r.Bounds.Left, r.Bounds.Top + shift, r.Bounds.Right, r.Bounds.Bottom + shift), r.PixelCount, RegionKind.Changed)));
            }
            else
            {
                bands.Add(new Band { Rows = rows, OutY = outY });
                int width = rows.AY >= 0 ? a.Width : b.Width;
                int left = rows.AY >= 0 ? 0 : dx;
                found.Add((new SKRectI(left, outY, left + width, outY + rows.Height), width * rows.Height, rows.Kind));
                if (rows.AY >= 0)
                {
                    onlyA += (long)width * rows.Height;
                }
                else
                {
                    onlyB += (long)width * rows.Height;
                }
            }
            outY += rows.Height;
        }

        var numbered = found.OrderBy(r => r.Bounds.Top / 16).ThenBy(r => r.Bounds.Left)
            .Select((r, i) => new DiffRegion(i + 1, r.Bounds, r.Pixels, r.Kind)).ToList();
        int added = rowBands.Where(r => r.Kind == RegionKind.OnlyInB).Sum(r => r.Height);
        int removed = rowBands.Where(r => r.Kind == RegionKind.OnlyInA).Sum(r => r.Height);
        var stats = new DiffStats
        {
            Align = AlignMode.Rows,
            OffsetB = new SKPointI(dx, 0),
            DiffPixels = diffPixels,
            IdenticalPercent = compared > 0 ? 100.0 * (compared - diffPixels) / compared : 0,
            Ssim = double.NaN,
            OnlyInA = onlyA,
            OnlyInB = onlyB,
            RegionCount = numbered.Count,
            Elapsed = watch.Elapsed,
            AlignNote = added == 0 && removed == 0
                ? "Căn theo dòng: không có đoạn thêm / bớt"
                : $"Căn theo dòng: B thêm {added} dòng, bỏ {removed} dòng so với A",
        };
        return new RowDiffView(a, b, dx, bands, numbered, stats);
    }

    public (SKRectI? InA, SKRectI? InB) SourceRects(DiffRegion region)
    {
        var band = _bands.LastOrDefault(b => b.OutY <= region.Bounds.Top) ?? _bands[0];
        var rows = band.Rows;
        int top = region.Bounds.Top - band.OutY, bottom = region.Bounds.Bottom - band.OutY;
        SKRectI? inA = rows.AY >= 0 ? new SKRectI(region.Bounds.Left, rows.AY + top, region.Bounds.Right, rows.AY + bottom) : null;
        SKRectI? inB = rows.BY >= 0 ? new SKRectI(region.Bounds.Left - _dx, rows.BY + top, region.Bounds.Right - _dx, rows.BY + bottom) : null;
        return (inA, inB);
    }

    public void Draw(SKCanvas canvas, float pixelSize, SKFilterQuality quality, int highlight = 0)
    {
        using var image = new SKPaint { FilterQuality = quality };
        using var wash = new SKPaint { Color = SKColors.White.WithAlpha(165) };
        using var overlayPaint = new SKPaint { FilterQuality = SKFilterQuality.None };
        foreach (var band in _bands)
        {
            var rows = band.Rows;
            if (rows.IsPaired || rows.BY >= 0)
            {
                // Dải có B: vẽ đúng các dòng của B.
                var dest = SKRect.Create(_dx, band.OutY, _b.Width, rows.Height);
                canvas.DrawBitmap(_b, SKRect.Create(0, rows.BY, _b.Width, rows.Height), dest, image);
                canvas.DrawRect(dest, wash);
                if (!rows.IsPaired)
                {
                    Tint(canvas, dest, DiffPainter.OnlyBColor, pixelSize);
                }
            }
            else
            {
                var dest = SKRect.Create(0, band.OutY, _a.Width, rows.Height);
                canvas.DrawBitmap(_a, SKRect.Create(0, rows.AY, _a.Width, rows.Height), dest, image);
                canvas.DrawRect(dest, wash);
                Tint(canvas, dest, DiffPainter.OnlyAColor, pixelSize);
            }
            if (band.Mask is { } mask)
            {
                LazyInitializer.EnsureInitialized(ref band.Overlay, () => DiffPainter.MaskToOverlay(mask, band.Overlap.Width, band.Overlap.Height));
                canvas.DrawBitmap(band.Overlay!, SKRect.Create(band.Overlap.Left, band.OutY, band.Overlap.Width, band.Overlap.Height), overlayPaint);
            }
        }
        DiffPainter.DrawRegionBoxes(canvas, _regions, pixelSize, highlight, Bounds.Top);
    }

    private static void Tint(SKCanvas canvas, SKRect area, SKColor color, float pixelSize)
    {
        using var tint = new SKPaint { Color = color.WithAlpha(50) };
        canvas.DrawRect(area, tint);
        using var edge = new SKPaint { Color = color, StrokeWidth = 3 * pixelSize };
        canvas.DrawLine(area.Left, area.Top, area.Left, area.Bottom, edge); // vạch màu mép trái như thanh đánh dấu diff
    }

    public SKBitmap Render()
    {
        var bounds = Bounds;
        var bmp = new SKBitmap(new SKImageInfo(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height), ImageUtil.ColorType, ImageUtil.AlphaType));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        canvas.Translate(-bounds.Left, -bounds.Top);
        Draw(canvas, 1, SKFilterQuality.None);
        return bmp;
    }

    public void Dispose()
    {
        foreach (var band in _bands)
        {
            band.Overlay?.Dispose();
        }
    }
}
