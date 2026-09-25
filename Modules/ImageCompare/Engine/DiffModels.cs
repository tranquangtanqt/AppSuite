using SkiaSharp;

namespace ImageCompare.Engine;

public enum AlignMode
{
    /// <summary>Đặt 2 ảnh trùng góc trên-trái.</summary>
    None,
    /// <summary>Tự tìm độ dịch (dx, dy) để 2 ảnh khớp nhau nhất - ảnh chụp lệch vài px / khác lề.</summary>
    Translate,
    /// <summary>Giữ nguyên độ dịch người dùng chỉnh tay (Alt + phím mũi tên).</summary>
    Manual,
    /// <summary>Căn từng dòng (trang dài: B thêm / bớt 1 đoạn ở giữa so với A) - xem <see cref="RowAligner"/>.</summary>
    Rows,
}

public enum RegionKind
{
    /// <summary>Có ở cả 2 ảnh nhưng khác pixel.</summary>
    Changed,
    /// <summary>Đoạn chỉ có ở A (B đã bỏ đi) - chỉ có ở chế độ căn theo dòng.</summary>
    OnlyInA,
    /// <summary>Đoạn chỉ có ở B (B thêm vào) - chỉ có ở chế độ căn theo dòng.</summary>
    OnlyInB,
}

public sealed record DiffOptions
{
    public AlignMode Align { get; init; } = AlignMode.Translate;
    /// <summary>Độ dịch dùng khi <see cref="Align"/> = Manual.</summary>
    public SKPointI ManualOffset { get; init; }
    /// <summary>Ngưỡng màu 0–100 (%): 2 pixel coi là khác khi chênh lệch lớn nhất giữa 3 kênh màu vượt
    /// ngưỡng này (theo thang 0–255). 0 = khác 1 đơn vị màu cũng tính.</summary>
    public double ThresholdPercent { get; init; } = 8;
    /// <summary>Bỏ qua khác biệt chỉ do viền chữ / khử răng cưa lệch 1 px (xem <see cref="PixelDiff"/>).</summary>
    public bool IgnoreAntialiasing { get; init; } = true;
    /// <summary>Vùng bỏ qua (toạ độ ảnh A) - đồng hồ, ngày giờ, avatar... khác nhau nhưng không quan trọng.</summary>
    public IReadOnlyList<SKRectI> IgnoreRects { get; init; } = [];

    public int ThresholdValue => (int)Math.Round(Math.Clamp(ThresholdPercent, 0, 100) * 255 / 100);
}

/// <summary>1 vùng khác nhau, đánh số từ 1 theo thứ tự trên → dưới. <see cref="Bounds"/> theo toạ độ của ảnh
/// khác biệt (<see cref="IDiffView.Bounds"/>): căn dịch chuyển = toạ độ ảnh A; căn theo dòng = toạ độ ảnh ghép.</summary>
public sealed record DiffRegion(int Number, SKRectI Bounds, int PixelCount, RegionKind Kind = RegionKind.Changed)
{
    public string KindText => Kind switch
    {
        RegionKind.OnlyInA => "chỉ có ở A (B bỏ đi)",
        RegionKind.OnlyInB => "chỉ có ở B (B thêm vào)",
        _ => "khác nhau",
    };
}

/// <summary>Số liệu tổng hợp của 1 lần so sánh - dùng chung cho mọi cách căn (bảng kết quả, báo cáo HTML).</summary>
public sealed record DiffStats
{
    public required AlignMode Align { get; init; }
    public required SKPointI OffsetB { get; init; }
    public required long DiffPixels { get; init; }
    /// <summary>% pixel giống nhau trong phần 2 ảnh được so với nhau.</summary>
    public required double IdenticalPercent { get; init; }
    /// <summary>SSIM (0–1); NaN khi không tính (căn theo dòng).</summary>
    public required double Ssim { get; init; }
    public required long OnlyInA { get; init; }
    public required long OnlyInB { get; init; }
    public required int RegionCount { get; init; }
    public required TimeSpan Elapsed { get; init; }
    /// <summary>Mô tả cách 2 ảnh đã được căn, vd "B lệch (−5, −12) px" / "B thêm 150 dòng ở y = 600".</summary>
    public required string AlignNote { get; init; }

    public bool IsIdentical => DiffPixels == 0 && OnlyInA == 0 && OnlyInB == 0;
}

/// <summary>Kết quả so sánh có thể vẽ / xuất - cài bởi <see cref="DiffPainter"/> (căn dịch chuyển) và
/// <see cref="RowDiffView"/> (căn theo dòng).</summary>
public interface IDiffView : IDisposable
{
    DiffStats Stats { get; }
    IReadOnlyList<DiffRegion> Regions { get; }
    /// <summary>Khung của ảnh khác biệt (toạ độ vẽ).</summary>
    SKRectI Bounds { get; }

    /// <param name="pixelSize">1 pixel màn hình theo đơn vị ảnh (1 / zoom) - giữ nét khung / cỡ chữ.</param>
    /// <param name="highlight">Số vùng đang chọn (vẽ đậm), 0 = không.</param>
    void Draw(SKCanvas canvas, float pixelSize, SKFilterQuality quality, int highlight = 0);

    /// <summary>Ảnh khác biệt 1:1 (Copy / Lưu PNG / báo cáo).</summary>
    SKBitmap Render();

    /// <summary>Vùng tương ứng trong ảnh A và B gốc (null nếu vùng không có ở ảnh đó) - cắt ảnh cho báo cáo.</summary>
    (SKRectI? InA, SKRectI? InB) SourceRects(DiffRegion region);
}

public sealed class DiffResult
{
    /// <summary>Vị trí ảnh B so với ảnh A (px ảnh A) đã dùng để so.</summary>
    public required SKPointI OffsetB { get; init; }
    /// <summary>Phần 2 ảnh chồng lên nhau (toạ độ ảnh A) - chỉ phần này được so từng pixel.</summary>
    public required SKRectI Overlap { get; init; }
    /// <summary>Mặt nạ khác biệt của phần chồng nhau: 1 byte / pixel, khác 0 = khác nhau. Kích thước =
    /// Overlap.Width × Overlap.Height.</summary>
    public required byte[] Mask { get; init; }
    public required IReadOnlyList<DiffRegion> Regions { get; init; }
    public required long DiffPixels { get; init; }
    /// <summary>% pixel giống nhau trong phần chồng nhau.</summary>
    public required double IdenticalPercent { get; init; }
    /// <summary>SSIM (0–1) trên ảnh xám thu nhỏ của phần chồng nhau - chịu được nén JPG / đổi cỡ nhẹ.</summary>
    public required double Ssim { get; init; }
    /// <summary>Số pixel chỉ có ở A / chỉ có ở B (khi 2 ảnh khác kích thước hoặc bị dịch).</summary>
    public required long OnlyInA { get; init; }
    public required long OnlyInB { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public AlignMode Align { get; init; } = AlignMode.None;
    /// <summary>Vùng bỏ qua đã dùng (toạ độ ảnh A) - vẽ sọc xám trên ảnh khác biệt.</summary>
    public IReadOnlyList<SKRectI> IgnoreRects { get; init; } = [];

    public bool IsIdentical => DiffPixels == 0 && OnlyInA == 0 && OnlyInB == 0;

    public DiffStats Stats => new()
    {
        Align = Align,
        OffsetB = OffsetB,
        DiffPixels = DiffPixels,
        IdenticalPercent = IdenticalPercent,
        Ssim = Ssim,
        OnlyInA = OnlyInA,
        OnlyInB = OnlyInB,
        RegionCount = Regions.Count,
        Elapsed = Elapsed,
        AlignNote = Align switch
        {
            AlignMode.None => "Không căn (trùng góc trên-trái)",
            AlignMode.Manual => $"Chỉnh tay: B lệch ({OffsetB.X}, {OffsetB.Y}) px",
            _ => $"Tự căn: B lệch ({OffsetB.X}, {OffsetB.Y}) px",
        },
    };
}
