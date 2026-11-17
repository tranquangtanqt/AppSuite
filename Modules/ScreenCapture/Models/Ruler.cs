using System.Globalization;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace ScreenCapture.Models;

/// <summary>Chế độ của Thước đo trên màn hình (2026-11-17).</summary>
public enum RulerMode
{
    /// <summary>Rê chuột: vạch ngang / dọc tự dừng ở chỗ màu đổi → khoảng trống giữa các thành phần.</summary>
    Edges,
    /// <summary>Kéo chữ nhật: rộng × cao.</summary>
    Box,
    /// <summary>Kéo đoạn thẳng A → B: Δx, Δy, độ dài.</summary>
    Line,
}

/// <summary>
/// 1 số đo, toạ độ pixel vật lý trên ảnh màn hình (gốc = góc trái trên màn hình ảo).
/// Edges: [X1..X2] × [Y1..Y2] là vùng cùng màu quanh điểm (CursorX, CursorY), tính cả 2 đầu.
/// Box: chữ nhật phủ pixel X1..X2, Y1..Y2 (tính cả 2 đầu). Line: từ tâm pixel (X1, Y1) tới tâm (X2, Y2).
/// <paramref name="Scale"/> = tỉ lệ DPI của màn hình chứa số đo (1.5 = 150%) để ghi kèm pixel logic.
/// </summary>
public sealed record RulerMeasurement(RulerMode Mode, int X1, int Y1, int X2, int Y2, int CursorX = 0, int CursorY = 0, double Scale = 1.0)
{
    public int Width => Mode == RulerMode.Line ? Math.Abs(X2 - X1) : Math.Abs(X2 - X1) + 1;
    public int Height => Mode == RulerMode.Line ? Math.Abs(Y2 - Y1) : Math.Abs(Y2 - Y1) + 1;
    public double Length => Math.Sqrt((double)Width * Width + (double)Height * Height);

    /// <summary>Nhãn chính (pixel vật lý), vd "120 × 32 px" / "Δx 100 · Δy 40 · 107,7 px".</summary>
    public string Label => Mode == RulerMode.Line
        ? $"Δx {Width} · Δy {Height} · {Ruler.Number(Length)} px"
        : $"{Width} × {Height} px";

    /// <summary>Dòng phụ pixel logic (null khi màn hình 100%), vd "logic 80 × 21,3 (150%)".</summary>
    public string? LogicalLabel
    {
        get
        {
            if (Math.Abs(Scale - 1.0) < 0.001 || Scale <= 0)
            {
                return null;
            }
            string percent = $"({Math.Round(Scale * 100)}%)";
            return Mode == RulerMode.Line
                ? $"logic {Ruler.Number(Length / Scale)} {percent}"
                : $"logic {Ruler.Number(Width / Scale)} × {Ruler.Number(Height / Scale)} {percent}";
        }
    }

    /// <summary>Chữ khi Copy (Ctrl+C).</summary>
    public string CopyText => LogicalLabel is { } logical ? $"{Label} ({logical})" : Label;
}

/// <summary>Logic thuần của Thước đo: dò cạnh trên ảnh màn hình, chuẩn hoá số đo, vẽ số đo lên ảnh (Mở trong Editor).</summary>
public static class Ruler
{
    public const int DefaultTolerance = 30;

    /// <summary>Số gọn kiểu Việt: 1 chữ số thập phân, dấu phẩy, bỏ ",0".</summary>
    public static string Number(double value)
    {
        double rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        return rounded.ToString("0.#", CultureInfo.GetCultureInfo("vi-VN"));
    }

    /// <summary>2 màu "giống nhau" khi mọi kênh R / G / B lệch không quá <paramref name="tolerance"/> (0 = đúng màu).</summary>
    public static bool Similar(uint a, uint b, int tolerance)
    {
        for (int shift = 0; shift < 24; shift += 8)
        {
            int ca = (int)((a >> shift) & 0xFF), cb = (int)((b >> shift) & 0xFF);
            if (Math.Abs(ca - cb) > tolerance)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Từ điểm (x, y) đi sang trái / phải / lên / xuống tới khi gặp pixel khác màu điểm đó (lệch quá <paramref name="tolerance"/>)
    /// hoặc mép ảnh → vùng cùng màu [Left..Right] × [Top..Bottom] (tính cả 2 đầu) = khoảng trống giữa các thành phần.
    /// <paramref name="pixels"/>: mỗi pixel 1 uint (BGRA / RGBA đều được - chỉ so 3 kênh màu), hàng nối tiếp.
    /// </summary>
    public static RulerMeasurement FindEdges(ReadOnlySpan<uint> pixels, int width, int height, int x, int y, int tolerance, double scale = 1.0)
    {
        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);
        uint origin = pixels[y * width + x];
        int left = x, right = x, top = y, bottom = y;
        while (left > 0 && Similar(pixels[y * width + left - 1], origin, tolerance)) left--;
        while (right < width - 1 && Similar(pixels[y * width + right + 1], origin, tolerance)) right++;
        while (top > 0 && Similar(pixels[(top - 1) * width + x], origin, tolerance)) top--;
        while (bottom < height - 1 && Similar(pixels[(bottom + 1) * width + x], origin, tolerance)) bottom++;
        return new RulerMeasurement(RulerMode.Edges, left, top, right, bottom, x, y, scale);
    }

    public static RulerMeasurement FindEdges(SKBitmap bitmap, int x, int y, int tolerance, double scale = 1.0) =>
        FindEdges(MemoryMarshal.Cast<byte, uint>(bitmap.GetPixelSpan()), bitmap.Width, bitmap.Height, x, y, tolerance, scale);

    /// <summary>Giữ Shift khi kéo đoạn thẳng: dính thành đường ngang hoặc dọc (theo chiều dài hơn).</summary>
    public static (int X, int Y) SnapLine(int ax, int ay, int bx, int by) =>
        Math.Abs(bx - ax) >= Math.Abs(by - ay) ? (bx, ay) : (ax, by);

    /// <summary>Chữ nhật từ 2 góc kéo (thứ tự bất kỳ) → X1 ≤ X2, Y1 ≤ Y2.</summary>
    public static RulerMeasurement Box(int ax, int ay, int bx, int by, double scale = 1.0) =>
        new(RulerMode.Box, Math.Min(ax, bx), Math.Min(ay, by), Math.Max(ax, bx), Math.Max(ay, by), Scale: scale);

    public static readonly SKColor LineColor = new(0xFF, 0x2D, 0x8C); // hồng đậm: nổi trên giao diện trắng / xám / tối
    private static readonly SKColor HaloColor = new(0, 0, 0, 110);

    /// <summary>
    /// Vẽ các số đo (đường + nhãn) lên <paramref name="canvas"/> theo toạ độ pixel vật lý. Dùng cho cả lớp phủ lúc đo và ảnh
    /// mở trong Editor. <paramref name="ui"/> = hệ số phóng chữ / nét (tỉ lệ DPI của cửa sổ) để nhãn không quá nhỏ ở 150%.
    /// </summary>
    /// <param name="bounds">Vùng nhãn phải nằm trong (theo toạ độ của canvas), mặc định = cả canvas. Editor truyền khung ảnh vì canvas
    /// ở đó đã dời + phóng theo zoom.</param>
    public static void Draw(SKCanvas canvas, IEnumerable<RulerMeasurement> measurements, float ui = 1f, SKRect? bounds = null)
    {
        var labelBounds = bounds ?? canvas.DeviceClipBounds;
        using var halo = new SKPaint { Color = HaloColor, StrokeWidth = 3 * ui, Style = SKPaintStyle.Stroke, IsAntialias = false };
        // ui < 1: Editor đang phóng to (nét tính theo pixel ảnh) → nét mảnh theo đúng tỉ lệ thay vì dày 1 pixel ảnh.
        using var line = new SKPaint { Color = LineColor, StrokeWidth = ui >= 1 ? MathF.Round(ui) : ui, Style = SKPaintStyle.Stroke, IsAntialias = false };
        using var fill = new SKPaint { Color = LineColor.WithAlpha(36), Style = SKPaintStyle.Fill };
        foreach (var m in measurements)
        {
            float tick = 6 * ui;
            switch (m.Mode)
            {
                case RulerMode.Edges:
                {
                    float cy = m.CursorY + 0.5f, cx = m.CursorX + 0.5f;
                    float l = m.X1, r = m.X2 + 1, t = m.Y1, b = m.Y2 + 1;
                    Segments(canvas, [(l, cy, r, cy), (l, cy - tick, l, cy + tick), (r, cy - tick, r, cy + tick),
                                      (cx, t, cx, b), (cx - tick, t, cx + tick, t), (cx - tick, b, cx + tick, b)], halo, line);
                    DrawLabel(canvas, m, cx + 10 * ui, cy + 10 * ui, cy - 10 * ui, ui, anchorCenter: false, labelBounds);
                    break;
                }
                case RulerMode.Box:
                {
                    var rect = new SKRect(m.X1, m.Y1, m.X2 + 1, m.Y2 + 1);
                    canvas.DrawRect(rect, fill);
                    canvas.DrawRect(rect, halo);
                    canvas.DrawRect(new SKRect(rect.Left + 0.5f, rect.Top + 0.5f, rect.Right - 0.5f, rect.Bottom - 0.5f), line);
                    DrawLabel(canvas, m, rect.MidX, rect.Bottom + 6 * ui, rect.Top - 6 * ui, ui, anchorCenter: true, labelBounds);
                    break;
                }
                case RulerMode.Line:
                {
                    float ax = m.X1 + 0.5f, ay = m.Y1 + 0.5f, bx = m.X2 + 0.5f, by = m.Y2 + 0.5f;
                    float dx = bx - ax, dy = by - ay, len = MathF.Max(1, MathF.Sqrt(dx * dx + dy * dy));
                    float nx = -dy / len * tick, ny = dx / len * tick; // vạch đầu mút vuông góc với đoạn
                    Segments(canvas, [(ax, ay, bx, by), (ax - nx, ay - ny, ax + nx, ay + ny), (bx - nx, by - ny, bx + nx, by + ny)], halo, line);
                    DrawLabel(canvas, m, (ax + bx) / 2, (ay + by) / 2 + 8 * ui, (ay + by) / 2 - 8 * ui, ui, anchorCenter: true, labelBounds);
                    break;
                }
            }
        }
    }

    private static void Segments(SKCanvas canvas, (float X1, float Y1, float X2, float Y2)[] segments, SKPaint halo, SKPaint line)
    {
        foreach (var s in segments)
        {
            canvas.DrawLine(s.X1, s.Y1, s.X2, s.Y2, halo);
        }
        foreach (var s in segments)
        {
            canvas.DrawLine(s.X1, s.Y1, s.X2, s.Y2, line);
        }
    }

    /// <summary>Nhãn nền tối chữ trắng, đặt dưới số đo (đỉnh nhãn ở <paramref name="y"/>); không đủ chỗ ở dưới thì lên trên
    /// (đáy nhãn ở <paramref name="yAbove"/>) để không đè lên vạch đo. Luôn nằm trong ảnh (đẩy vào khi sát mép).</summary>
    private static void DrawLabel(SKCanvas canvas, RulerMeasurement m, float x, float y, float yAbove, float ui, bool anchorCenter, SKRect bounds)
    {
        using var bold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
        using var regular = SKTypeface.FromFamilyName("Segoe UI");
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true, Typeface = bold, TextSize = 13 * ui };
        using var dim = new SKPaint { Color = new SKColor(255, 255, 255, 200), IsAntialias = true, Typeface = regular, TextSize = 11 * ui };
        using var back = new SKPaint { Color = new SKColor(24, 24, 24, 225), IsAntialias = true };

        string main = m.Label;
        string? sub = m.LogicalLabel;
        float pad = 6 * ui;
        float width = white.MeasureText(main);
        if (sub is not null)
        {
            width = Math.Max(width, dim.MeasureText(sub));
        }
        float height = white.FontSpacing + (sub is null ? 0 : dim.FontSpacing);
        float left = anchorCenter ? x - width / 2 - pad : x;
        var box = new SKRect(left, y, left + width + 2 * pad, y + height + 2 * pad);
        if (box.Right > bounds.Right) box.Offset(bounds.Right - box.Right, 0);
        if (box.Bottom > bounds.Bottom) box.Offset(0, yAbove - box.Bottom);
        if (box.Bottom > bounds.Bottom) box.Offset(0, bounds.Bottom - box.Bottom);
        if (box.Left < bounds.Left) box.Offset(bounds.Left - box.Left, 0);
        if (box.Top < bounds.Top) box.Offset(0, bounds.Top - box.Top);

        canvas.DrawRoundRect(box, 4 * ui, 4 * ui, back);
        float baseline = box.Top + pad - white.FontMetrics.Ascent;
        canvas.DrawText(main, box.Left + pad, baseline, white);
        if (sub is not null)
        {
            canvas.DrawText(sub, box.Left + pad, baseline + white.FontMetrics.Descent - dim.FontMetrics.Ascent, dim);
        }
    }

    /// <summary>
    /// Bước vạch có số của thước kẻ ở mép khung ảnh (Editor): số "đẹp" 1 / 2 / 5 × 10^k nhỏ nhất sao cho 2 vạch có số cách
    /// nhau ít nhất <paramref name="minGap"/> trên màn hình. <paramref name="pixelsPerUnit"/> = 1 pixel ảnh chiếm bao nhiêu
    /// đơn vị màn hình (zoom).
    /// </summary>
    public static int TickStep(double pixelsPerUnit, double minGap)
    {
        if (pixelsPerUnit <= 0)
        {
            return 1;
        }
        double needed = minGap / pixelsPerUnit;
        for (long magnitude = 1; magnitude <= 100_000_000; magnitude *= 10)
        {
            foreach (int m in (ReadOnlySpan<int>)[1, 2, 5])
            {
                if (m * magnitude >= needed)
                {
                    return (int)(m * magnitude);
                }
            }
        }
        return 1_000_000_000;
    }

    /// <summary>Vạch nhỏ giữa 2 vạch có số: chia 10 / 5 / 2 (vạch nhỏ cách nhau ≥ <paramref name="minGap"/>), 0 = không có.</summary>
    public static int MinorTickStep(int step, double pixelsPerUnit, double minGap)
    {
        foreach (int parts in (ReadOnlySpan<int>)[10, 5, 2])
        {
            if (step % parts == 0 && step / parts * pixelsPerUnit >= minGap)
            {
                return step / parts;
            }
        }
        return 0;
    }

    /// <summary>
    /// Editor → "Chèn vào ảnh": 1 số đo thành các hình vẽ thường (sửa / xoá / di chuyển được như hình tự vẽ): chữ nhật hoặc
    /// đường có mũi tên 2 đầu (màu hồng của thước) + ô chữ ghi số đo (nền tối chữ trắng). Ô chữ nằm dưới số đo, không đủ chỗ
    /// thì lên trên, luôn trong <paramref name="image"/>.
    /// </summary>
    public static List<AnnotationShape> ToAnnotations(RulerMeasurement m, SKRect image)
    {
        const float stroke = 2f;
        var shapes = new List<AnnotationShape>();
        float labelX, labelY, labelAbove;
        bool center;
        switch (m.Mode)
        {
            case RulerMode.Box:
            {
                var rect = new SKRect(m.X1, m.Y1, m.X2 + 1, m.Y2 + 1);
                shapes.Add(new RectangleAnnotation { Bounds = rect, Color = LineColor, StrokeWidth = stroke });
                (labelX, labelY, labelAbove, center) = (rect.MidX, rect.Bottom + 6, rect.Top - 6, true);
                break;
            }
            case RulerMode.Line:
            {
                var a = new SKPoint(m.X1 + 0.5f, m.Y1 + 0.5f);
                var b = new SKPoint(m.X2 + 0.5f, m.Y2 + 0.5f);
                shapes.Add(DimensionLine(a, b));
                float midY = (a.Y + b.Y) / 2;
                (labelX, labelY, labelAbove, center) = ((a.X + b.X) / 2, midY + 8, midY - 8, true);
                break;
            }
            default:
            {
                float cx = m.CursorX + 0.5f, cy = m.CursorY + 0.5f;
                shapes.Add(DimensionLine(new SKPoint(m.X1, cy), new SKPoint(m.X2 + 1, cy)));
                shapes.Add(DimensionLine(new SKPoint(cx, m.Y1), new SKPoint(cx, m.Y2 + 1)));
                (labelX, labelY, labelAbove, center) = (cx + 10, cy + 10, cy - 10, false);
                break;
            }
        }

        var label = new TextAnnotation
        {
            Text = m.Label,
            FontSize = 16,
            Bold = true,
            Color = SKColors.White,
            BackgroundColor = new SKColor(24, 24, 24, 225),
        };
        label.Bounds = SKRect.Create(labelX, labelY, 1, 1);
        label.FitBounds();
        var box = label.Bounds;
        if (center) box.Offset(-box.Width / 2, 0);
        if (box.Right > image.Right) box.Offset(image.Right - box.Right, 0);
        if (box.Bottom > image.Bottom) box.Offset(0, labelAbove - box.Bottom);
        if (box.Bottom > image.Bottom) box.Offset(0, image.Bottom - box.Bottom);
        if (box.Left < image.Left) box.Offset(image.Left - box.Left, 0);
        if (box.Top < image.Top) box.Offset(0, image.Top - box.Top);
        label.Bounds = box;
        shapes.Add(label);
        return shapes;
    }

    /// <summary>Đường kích thước: mũi tên 2 đầu; đoạn ngắn (mũi tên sẽ chồng nhau) thì không có mũi tên.</summary>
    private static LineArrowAnnotation DimensionLine(SKPoint a, SKPoint b)
    {
        bool arrows = SKPoint.Distance(a, b) >= 40;
        return new LineArrowAnnotation
        {
            Bounds = LineArrowAnnotation.FromPoints(a, b),
            Color = LineColor,
            StrokeWidth = 2f,
            StartHead = arrows ? ArrowHead.Arrow : ArrowHead.None,
            EndHead = arrows ? ArrowHead.Arrow : ArrowHead.None,
        };
    }
}
