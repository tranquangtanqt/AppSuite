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
        var rectA = SKRectI.Create(a.Width, a.Height);
        // Soi 1 vùng: vùng phải còn nằm trong ảnh A (đổi ảnh A khác cỡ) - không thì so như Tự căn chỉnh.
        var focus = options.Align == AlignMode.Focus && options.FocusRect is { } fr && SKRectI.Intersect(fr, rectA) is { Width: >= 2, Height: >= 2 } clipped
            ? clipped
            : (SKRectI?)null;
        var match = focus is { } f ? RegionAligner.Find(a, b, f, ct) : null;
        var offset = options.Align switch
        {
            AlignMode.Focus when match is not null => match.OffsetB,
            AlignMode.Translate or AlignMode.Focus => Aligner.FindOffset(a, b, ct),
            AlignMode.Manual => options.ManualOffset,
            _ => SKPointI.Empty,
        };

        var rectB = SKRectI.Create(offset.X, offset.Y, b.Width, b.Height);
        var overlap = SKRectI.Intersect(rectA, rectB);
        if (overlap.IsEmpty)
        {
            overlap = SKRectI.Empty;
        }
        // Soi 1 vùng: so cả vùng với ảnh B đã nắn theo từng dải (đặt đúng vị trí vùng trên A).
        var (compareB, compareOffset) = (b, offset);
        if (match is not null && focus is { } fo)
        {
            (compareB, compareOffset, overlap) = (match.Warped, new SKPointI(fo.Left, fo.Top), fo);
        }

        var (mask, diffPixels, regions) = PixelDiff.Compare(a, compareB, compareOffset, overlap, options, ct);
        long overlapPixels = (long)overlap.Width * overlap.Height;
        double ssim = overlapPixels > 0 ? Similarity.Ssim(a, compareB, compareOffset, overlap, ct) : 0;

        return new DiffResult
        {
            OffsetB = offset,
            Overlap = overlap,
            Mask = mask,
            Regions = regions,
            DiffPixels = diffPixels,
            IdenticalPercent = overlapPixels > 0 ? 100.0 * (overlapPixels - diffPixels) / overlapPixels : 0,
            Ssim = ssim,
            // Soi 1 vùng: phần ngoài vùng không so, không tính là "chỉ có ở A / B".
            OnlyInA = focus is null ? (long)a.Width * a.Height - overlapPixels : 0,
            OnlyInB = focus is null ? (long)b.Width * b.Height - overlapPixels : 0,
            Elapsed = watch.Elapsed,
            Align = focus is null && options.Align == AlignMode.Focus ? AlignMode.Translate : options.Align,
            IgnoreRects = options.IgnoreRects,
            Focus = focus,
            FocusMatch = match,
        };
    }
}
