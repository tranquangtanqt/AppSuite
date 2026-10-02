using System.Globalization;
using System.Net;
using System.Text;
using SkiaSharp;

namespace ImageCompare.Engine;

/// <summary>Xuất kết quả So chữ: dòng tách Tab (copy → dán thẳng vào Excel), file CSV, báo cáo HTML 1 file kèm ảnh cắt
/// A | B từng chỗ. Cột chung: #, Loại, Chữ A, Chữ B, Ghi chú (màu), Vị trí A, Vị trí B.</summary>
public static class TextDiffReport
{
    private static readonly string[] Header = ["#", "Loại", "Chữ A", "Chữ B", "Ghi chú", "Vị trí A (x, y, rộng, cao)", "Vị trí B (x, y, rộng, cao)"];

    public static string KindText(TextDiffKind kind) => kind switch
    {
        TextDiffKind.Changed => "Đổi chữ",
        TextDiffKind.OnlyInA => "Chỉ ở A",
        TextDiffKind.OnlyInB => "Chỉ ở B",
        TextDiffKind.ColorChanged => "Khác màu chữ",
        TextDiffKind.Similar => "Gần giống (nghi OCR)",
        _ => kind.ToString(),
    };

    /// <summary>Các ô của 1 mục. Mục chỉ có ở 1 phía: vị trí phía kia là chỗ dự đoán (ghi "≈").</summary>
    public static string[] Cells(TextDiffItem item) =>
    [
        item.Number.ToString(CultureInfo.InvariantCulture),
        KindText(item.Kind),
        item.A?.Text ?? string.Empty,
        item.B?.Text ?? string.Empty,
        item.Note ?? string.Empty,
        item.A is { } a ? Box(a.Bounds) : item.Other is { } oa ? "≈ " + Box(oa) : string.Empty,
        item.B is { } b ? Box(b.Bounds) : item.Other is { } ob ? "≈ " + Box(ob) : string.Empty,
    ];

    /// <summary>1 dòng tách Tab (bỏ Tab / xuống dòng trong chữ OCR để không vỡ cột).</summary>
    public static string TsvLine(TextDiffItem item) => string.Join('\t', Cells(item).Select(c => c.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ')));

    /// <summary>Cả danh sách tách Tab, có dòng tiêu đề.</summary>
    public static string Tsv(IEnumerable<TextDiffItem> items)
    {
        var sb = new StringBuilder(string.Join('\t', Header)).Append("\r\n");
        foreach (var item in items)
        {
            sb.Append(TsvLine(item)).Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>CSV theo RFC 4180 (ô có dấu phẩy / ngoặc kép / xuống dòng thì bọc "..."). Lưu kèm BOM UTF-8 để Excel
    /// nhận đúng tiếng Nhật / tiếng Việt.</summary>
    public static string Csv(IEnumerable<TextDiffItem> items)
    {
        var sb = new StringBuilder(string.Join(',', Header.Select(Quote))).Append("\r\n");
        foreach (var item in items)
        {
            sb.Append(string.Join(',', Cells(item).Select(Quote))).Append("\r\n");
        }
        return sb.ToString();

        static string Quote(string s) => s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    /// <param name="items">Các mục đang hiện (theo tuỳ chọn "Hiện gần giống").</param>
    /// <param name="readerName">Bộ đọc chữ đã dùng, ghi vào báo cáo.</param>
    public static string Html(string nameA, SKBitmap a, string nameB, SKBitmap b, TextDiffResult result,
        IReadOnlyList<TextDiffItem> items, string readerName)
    {
        var vi = CultureInfo.GetCultureInfo("vi-VN");
        string E(string s) => WebUtility.HtmlEncode(s);
        string verdict = items.Count == 0 ? "<span class=\"ok\">Chữ giống nhau</span>" : $"<span class=\"bad\">{items.Count} chỗ khác về chữ</span>";
        var sb = new StringBuilder();
        sb.Append($$"""
            <!DOCTYPE html>
            <html lang="vi"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>So chữ: {{E(nameA)}} ↔ {{E(nameB)}}</title>
            <style>
            body{font-family:"Segoe UI","Yu Gothic UI",system-ui,sans-serif;margin:0;padding:24px;color:#222;background:#f6f7f9}
            h1{font-size:22px;margin:0 0 4px} h2{font-size:17px;margin:28px 0 10px}
            .muted{color:#666;font-size:13px}
            table{border-collapse:collapse;background:#fff;box-shadow:0 1px 3px rgba(0,0,0,.08)}
            th,td{border:1px solid #e1e4e8;padding:6px 10px;text-align:left;vertical-align:top;font-size:14px}
            th{background:#f0f2f5;font-weight:600}
            .ok{color:#1a7f37;font-weight:600} .bad{color:#d1242f;font-weight:600}
            .img{max-width:100%;border:1px solid #d0d7de;background:#fff}
            .crop{max-width:360px;max-height:260px;border:1px solid #d0d7de;background:#fff;image-rendering:pixelated}
            .tagA{color:#2B7BD6;font-weight:600} .tagB{color:#D86445;font-weight:600}
            .kind{display:inline-block;color:#fff;border-radius:3px;padding:0 6px;font-weight:600;white-space:nowrap}
            details{margin-top:12px} summary{cursor:pointer;font-weight:600}
            </style></head><body>
            <h1>Báo cáo so chữ</h1>
            <div class="muted">Tạo lúc {{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", vi)}} bằng AppSuite · So sánh ảnh · So chữ</div>
            <h2>Kết luận: {{verdict}}</h2>
            <table>
            <tr><th>Ảnh A (gốc)</th><td class="tagA">{{E(nameA)}}</td><td>{{a.Width}} × {{a.Height}} px</td></tr>
            <tr><th>Ảnh B (mới)</th><td class="tagB">{{E(nameB)}}</td><td>{{b.Width}} × {{b.Height}} px</td></tr>
            <tr><th>Theo loại</th><td colspan="2">Đổi chữ: {{Count(TextDiffKind.Changed)}} · Chỉ ở A: {{Count(TextDiffKind.OnlyInA)}} · Chỉ ở B: {{Count(TextDiffKind.OnlyInB)}} · Khác màu chữ: {{Count(TextDiffKind.ColorChanged)}} · Gần giống: {{Count(TextDiffKind.Similar)}}</td></tr>
            <tr><th>Giống</th><td colspan="2">{{result.SameCount}} đoạn chữ (đọc được A: {{result.SegmentsA}}, B: {{result.SegmentsB}})</td></tr>
            <tr><th>Đọc chữ bằng</th><td colspan="2">{{E(readerName)}} - chữ đọc từ ảnh có thể sai, soi lại trên ảnh cắt</td></tr>
            </table>
            """);
        if (items.Count > 0)
        {
            sb.Append("<h2>Các chỗ khác về chữ</h2><table><tr><th>#</th><th>Loại</th><th>Chữ A</th><th>Chữ B</th><th>Ghi chú</th>"
                + "<th class=\"tagA\">A</th><th class=\"tagB\">B</th></tr>");
            foreach (var item in items)
            {
                // Mục chỉ có ở 1 phía: ảnh cắt phía kia lấy ở chỗ dự đoán - để người xem tự soi.
                SKRectI? boxA = item.A?.Bounds ?? item.Other, boxB = item.B?.Bounds ?? item.Other;
                sb.Append($"<tr><td><b>{item.Number}</b></td><td><span class=\"kind\" style=\"background:{KindColor(item.Kind)}\">{E(KindText(item.Kind))}</span></td>");
                sb.Append($"<td>{E(item.A?.Text ?? "")}</td><td>{E(item.B?.Text ?? "")}</td><td>{E(item.Note ?? "")}</td>");
                sb.Append($"<td>{HtmlReport.CropCell(a, boxA)}</td><td>{HtmlReport.CropCell(b, boxB)}</td></tr>");
            }
            sb.Append("</table>");
        }
        sb.Append($"<details><summary>Ảnh A gốc</summary><img class=\"img\" alt=\"Ảnh A\" src=\"{HtmlReport.DataUri(a)}\"></details>");
        sb.Append($"<details><summary>Ảnh B gốc</summary><img class=\"img\" alt=\"Ảnh B\" src=\"{HtmlReport.DataUri(b)}\"></details>");
        sb.Append("</body></html>");
        return sb.ToString();

        int Count(TextDiffKind kind) => result.Count(kind);
    }

    /// <summary>Màu theo loại - giống danh sách trong app (đổi chữ đỏ, chỉ A xanh, chỉ B cam, khác màu tím, gần giống xám).</summary>
    public static string KindColor(TextDiffKind kind) => kind switch
    {
        TextDiffKind.Changed => "#E51A1A",
        TextDiffKind.OnlyInA => "#2B7BD6",
        TextDiffKind.OnlyInB => "#D86445",
        TextDiffKind.ColorChanged => "#8250DF",
        _ => "#6E7781",
    };

    private static string Box(SKRectI r) => $"{r.Left}, {r.Top}, {r.Width}, {r.Height}";
}
