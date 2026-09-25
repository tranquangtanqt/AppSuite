using System.Diagnostics;
using SkiaSharp;

namespace ImageCompare.Engine;

/// <summary>Điểm vào của Engine: căn chỉnh → so pixel → độ giống. Chạy được ngoài UI thread, huỷ được.</summary>
public static class ImageComparer
{
    /// <summary>So sánh theo tuỳ chọn và trả về kết quả vẽ / xuất được. Căn theo dòng mà 2 ảnh khác nhau quá nhiều
    /// (không còn là cùng 1 trang) thì lui về tự căn dịch chuyển, ghi chú lại trong <see cref="DiffStats.AlignNote"/>.</summary>
    public static IDiffView CreateView(SKBitmap a, SKBitmap b, DiffOptions options, CancellationToken ct = default)
    {
        if (options.Align == AlignMode.Rows)
        {
            if (RowDiffView.Create(a, b, options, ct) is { } rows)
            {
                return rows;
            }
            var fallback = Compare(a, b, options with { Align = AlignMode.Translate }, ct);
            return new DiffPainter(a, b, fallback, "2 ảnh khác nhau quá nhiều để căn theo dòng - đã tự căn dịch chuyển");
        }
        return new DiffPainter(a, b, Compare(a, b, options, ct));
    }

    public static DiffResult Compare(SKBitmap a, SKBitmap b, DiffOptions options, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        var offset = options.Align switch
        {
            AlignMode.Translate => Aligner.FindOffset(a, b, ct),
            AlignMode.Manual => options.ManualOffset,
            _ => SKPointI.Empty,
        };

        var rectA = SKRectI.Create(a.Width, a.Height);
        var rectB = SKRectI.Create(offset.X, offset.Y, b.Width, b.Height);
        var overlap = SKRectI.Intersect(rectA, rectB);
        if (overlap.IsEmpty)
        {
            overlap = SKRectI.Empty;
        }

        var (mask, diffPixels, regions) = PixelDiff.Compare(a, b, offset, overlap, options, ct);
        long overlapPixels = (long)overlap.Width * overlap.Height;
        double ssim = overlapPixels > 0 ? Similarity.Ssim(a, b, offset, overlap, ct) : 0;

        return new DiffResult
        {
            OffsetB = offset,
            Overlap = overlap,
            Mask = mask,
            Regions = regions,
            DiffPixels = diffPixels,
            IdenticalPercent = overlapPixels > 0 ? 100.0 * (overlapPixels - diffPixels) / overlapPixels : 0,
            Ssim = ssim,
            OnlyInA = (long)a.Width * a.Height - overlapPixels,
            OnlyInB = (long)b.Width * b.Height - overlapPixels,
            Elapsed = watch.Elapsed,
            Align = options.Align,
            IgnoreRects = options.IgnoreRects,
        };
    }
}
