using System.Globalization;
using System.Net;
using System.Text;
using SkiaSharp;

namespace ImageCompare.Engine;

/// <summary>Báo cáo so sánh dạng 1 file HTML tự chứa (ảnh nhúng base64) - gửi người khác mở bằng trình duyệt,
/// không cần cài app. Gồm: thông tin 2 ảnh + tuỳ chọn, kết luận, ảnh khác biệt, bảng từng vùng khác kèm ảnh
/// cắt A | B, và 2 ảnh gốc (thu gọn).</summary>
public static class HtmlReport
{
    private const int MaxRegionCrops = 100;
    private const int CropPadding = 24;

    public static string Build(string nameA, SKBitmap a, string nameB, SKBitmap b, DiffOptions options, IDiffView view)
    {
        var r = view.Stats;
        var vi = CultureInfo.GetCultureInfo("vi-VN");
        string Num(double v, string format = "N0") => v.ToString(format, vi);
        string E(string s) => WebUtility.HtmlEncode(s);

        string verdict = r.IsIdentical
            ? "<span class=\"ok\">Giống hệt nhau</span>"
            : r.RegionCount > 0
                ? $"<span class=\"bad\">Có {r.RegionCount} chỗ khác nhau</span>"
                : "<span class=\"warn\">Phần chồng nhau giống hệt, nhưng 2 ảnh khác kích thước / vị trí</span>";
        string align = r.AlignNote;

        var sb = new StringBuilder();
        sb.Append($$"""
            <!DOCTYPE html>
            <html lang="vi"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>So sánh ảnh: {{E(nameA)}} ↔ {{E(nameB)}}</title>
            <style>
            body{font-family:"Segoe UI",system-ui,sans-serif;margin:0;padding:24px;color:#222;background:#f6f7f9}
            h1{font-size:22px;margin:0 0 4px} h2{font-size:17px;margin:28px 0 10px}
            .muted{color:#666;font-size:13px}
            table{border-collapse:collapse;background:#fff;box-shadow:0 1px 3px rgba(0,0,0,.08)}
            th,td{border:1px solid #e1e4e8;padding:6px 10px;text-align:left;vertical-align:top;font-size:14px}
            th{background:#f0f2f5;font-weight:600}
            .ok{color:#1a7f37;font-weight:600} .bad{color:#d1242f;font-weight:600} .warn{color:#9a6700;font-weight:600}
            .img{max-width:100%;border:1px solid #d0d7de;background:#fff}
            .crop{max-width:360px;max-height:260px;border:1px solid #d0d7de;background:#fff;image-rendering:pixelated}
            .tagA{color:#2B7BD6;font-weight:600} .tagB{color:#D86445;font-weight:600}
            details{margin-top:12px} summary{cursor:pointer;font-weight:600}
            </style></head><body>
            <h1>Báo cáo so sánh ảnh</h1>
            <div class="muted">Tạo lúc {{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", vi)}} bằng AppSuite · So sánh ảnh</div>
            <h2>Kết luận: {{verdict}}</h2>
            <table>
            <tr><th>Ảnh A (gốc)</th><td class="tagA">{{E(nameA)}}</td><td>{{a.Width}} × {{a.Height}} px</td></tr>
            <tr><th>Ảnh B (mới)</th><td class="tagB">{{E(nameB)}}</td><td>{{b.Width}} × {{b.Height}} px</td></tr>
            <tr><th>Căn chỉnh</th><td colspan="2">{{E(align)}}</td></tr>
            <tr><th>Ngưỡng màu</th><td colspan="2">{{Num(options.ThresholdPercent, "0.#")}}% · {{(options.IgnoreAntialiasing ? "bỏ qua khác biệt do khử răng cưa" : "tính cả khác biệt do khử răng cưa")}}</td></tr>
            <tr><th>Số vùng khác</th><td colspan="2">{{r.RegionCount}} vùng · {{Num(r.DiffPixels)}} pixel khác</td></tr>
            <tr><th>Pixel giống nhau</th><td colspan="2">{{Num(r.IdenticalPercent, "0.###")}}% (trên phần 2 ảnh được so với nhau)</td></tr>
            <tr><th>Độ giống cấu trúc (SSIM)</th><td colspan="2">{{(double.IsNaN(r.Ssim) ? "- (không tính khi căn theo dòng)" : Num(r.Ssim, "0.####") + " (1 = giống hệt)")}}</td></tr>
            """);
        if (r.OnlyInA > 0 || r.OnlyInB > 0)
        {
            sb.Append($"<tr><th>Chỉ có ở 1 ảnh</th><td colspan=\"2\">A: {Num(r.OnlyInA)} px · B: {Num(r.OnlyInB)} px (tô sọc trong ảnh khác biệt)</td></tr>");
        }
        sb.Append("</table>");

        using (var diff = view.Render())
        {
            sb.Append("<h2>Ảnh khác biệt</h2><div class=\"muted\">Nền: ảnh B làm nhạt · đỏ: pixel khác · khung số: vùng khác · xanh / cam: phần chỉ có ở A (bị bỏ) / chỉ có ở B (thêm vào).</div>");
            sb.Append($"<img class=\"img\" alt=\"Ảnh khác biệt\" src=\"{DataUri(diff)}\">");
        }

        var regions = view.Regions;
        if (regions.Count > 0)
        {
            sb.Append("<h2>Các vùng khác nhau</h2><table><tr><th>#</th><th>Loại</th><th>Vị trí (x, y)</th><th>Kích thước</th><th>Pixel khác</th><th class=\"tagA\">A</th><th class=\"tagB\">B</th></tr>");
            foreach (var region in regions.Take(MaxRegionCrops))
            {
                var box = region.Bounds;
                var (inA, inB) = view.SourceRects(region);
                sb.Append($"<tr><td><b>{region.Number}</b></td><td>{E(region.KindText)}</td><td>{box.Left}, {box.Top}</td><td>{box.Width} × {box.Height}</td><td>{Num(region.PixelCount)}</td>");
                sb.Append($"<td>{CropCell(a, inA)}</td><td>{CropCell(b, inB)}</td></tr>");
            }
            sb.Append("</table>");
            if (regions.Count > MaxRegionCrops)
            {
                sb.Append($"<div class=\"muted\">… và {regions.Count - MaxRegionCrops} vùng khác (xem ảnh khác biệt).</div>");
            }
        }

        sb.Append($"<details><summary>Ảnh A gốc</summary><img class=\"img\" alt=\"Ảnh A\" src=\"{DataUri(a)}\"></details>");
        sb.Append($"<details><summary>Ảnh B gốc</summary><img class=\"img\" alt=\"Ảnh B\" src=\"{DataUri(b)}\"></details>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>Ảnh cắt vùng (thêm lề <see cref="CropPadding"/>), hoặc ghi chú nếu vùng không có ở ảnh này.</summary>
    private static string CropCell(SKBitmap source, SKRectI? region)
    {
        if (region is not { } box)
        {
            return "<span class=\"muted\">(không có ở ảnh này)</span>";
        }
        var area = SKRectI.Intersect(SKRectI.Inflate(box, CropPadding, CropPadding), SKRectI.Create(source.Width, source.Height));
        if (area.IsEmpty)
        {
            return "<span class=\"muted\">(ngoài ảnh)</span>";
        }
        using var crop = new SKBitmap(new SKImageInfo(area.Width, area.Height, ImageUtil.ColorType, ImageUtil.AlphaType));
        using (var canvas = new SKCanvas(crop))
        {
            canvas.DrawBitmap(source, (SKRect)area, SKRect.Create(area.Width, area.Height));
        }
        return $"<img class=\"crop\" alt=\"\" src=\"{DataUri(crop)}\">";
    }

    private static string DataUri(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return "data:image/png;base64," + Convert.ToBase64String(data.AsSpan());
    }
}
