using System.Globalization;
using SkiaSharp;

namespace ScreenCapture.Models;

/// <summary>Định dạng mã màu khi copy (Bảng màu, Hút màu).</summary>
public enum ColorCopyFormat
{
    /// <summary>#757369</summary>
    Html,
    /// <summary>rgb(117, 115, 105)</summary>
    Rgb,
}

/// <summary>
/// Tính toán cho Bảng màu / Hút màu (2026-11-09): mã màu HTML / RGB, đổi RGB ↔ HSL (thang 0–255 như PicPick: H 35, S 14,
/// L 111 cho #757369), sắc độ của màu chủ đề (kiểu Office), danh sách màu tự chọn.
/// </summary>
public static class ColorTools
{
    /// <summary>Số ô Màu tự chọn (2 hàng × 10). Đầy thì màu cũ nhất bị đẩy ra.</summary>
    public const int MaxCustomColors = 20;

    /// <summary>Màu chủ đề (hàng trên cùng, giống bảng màu của Paint / PicPick); dưới mỗi màu có 5 sắc độ (<see cref="Shades"/>).</summary>
    public static IReadOnlyList<SKColor> ThemeColors { get; } =
    [
        new(0x00, 0x00, 0x00), new(0xFF, 0xFF, 0xFF), new(0x88, 0x00, 0x15), new(0xED, 0x1C, 0x24), new(0xFF, 0x7F, 0x27),
        new(0xFF, 0xF2, 0x00), new(0x22, 0xB1, 0x4C), new(0x00, 0xA2, 0xE8), new(0x3F, 0x48, 0xCC), new(0xA3, 0x49, 0xA4),
    ];

    /// <summary>Màu chuẩn (như Office).</summary>
    public static IReadOnlyList<SKColor> StandardColors { get; } =
    [
        new(0xC0, 0x00, 0x00), new(0xFF, 0x00, 0x00), new(0xFF, 0xC0, 0x00), new(0xFF, 0xFF, 0x00), new(0x92, 0xD0, 0x50),
        new(0x00, 0xB0, 0x50), new(0x00, 0xB0, 0xF0), new(0x00, 0x70, 0xC0), new(0x00, 0x20, 0x60), new(0x70, 0x30, 0xA0),
    ];

    public static string ToHtml(SKColor color) => $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

    public static string ToRgb(SKColor color) => $"rgb({color.Red}, {color.Green}, {color.Blue})";

    public static string Format(SKColor color, ColorCopyFormat format) => format == ColorCopyFormat.Rgb ? ToRgb(color) : ToHtml(color);

    /// <summary>Đọc "#RRGGBB" / "RRGGBB" / "#RGB" (có thể có khoảng trắng). Màu luôn đục (alpha 255).</summary>
    public static bool TryParseHtml(string? text, out SKColor color)
    {
        color = SKColors.Black;
        var hex = (text ?? string.Empty).Trim().TrimStart('#');
        if (hex.Length == 3)
        {
            hex = string.Concat(hex.Select(c => $"{c}{c}"));
        }
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
        {
            return false;
        }
        color = new SKColor((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    /// <summary>RGB → HSL, mỗi thành phần 0–255 (H: 255 = 360°).</summary>
    public static (int H, int S, int L) ToHsl(SKColor color)
    {
        var (h, s, l) = ToHslDouble(color);
        return ((int)Math.Round(h / 360 * 255) % 256, (int)Math.Round(s * 255), (int)Math.Round(l * 255));
    }

    /// <summary>HSL (mỗi thành phần 0–255) → RGB.</summary>
    public static SKColor FromHsl(int h, int s, int l) =>
        FromHslDouble(Math.Clamp(h, 0, 255) / 255.0 * 360, Math.Clamp(s, 0, 255) / 255.0, Math.Clamp(l, 0, 255) / 255.0);

    /// <summary>HSL thật: H 0–360°, S / L 0–1.</summary>
    public static (double H, double S, double L) ToHslDouble(SKColor color)
    {
        double r = color.Red / 255.0, g = color.Green / 255.0, b = color.Blue / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2;
        if (max - min < 1e-9)
        {
            return (0, 0, l);
        }
        double d = max - min;
        double s = l <= 0.5 ? d / (max + min) : d / (2 - max - min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0)
            : max == g ? (b - r) / d + 2
            : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    public static SKColor FromHslDouble(double h, double s, double l)
    {
        if (s <= 0)
        {
            byte gray = ToByte(l);
            return new SKColor(gray, gray, gray);
        }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        double hk = (h % 360 + 360) % 360 / 360;
        return new SKColor(ToByte(HueToRgb(p, q, hk + 1.0 / 3)), ToByte(HueToRgb(p, q, hk)), ToByte(HueToRgb(p, q, hk - 1.0 / 3)));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        t = (t % 1 + 1) % 1;
        return t < 1.0 / 6 ? p + (q - p) * 6 * t
            : t < 0.5 ? q
            : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6
            : p;
    }

    private static byte ToByte(double v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);

    /// <summary>5 sắc độ dưới 1 màu chủ đề, sáng → tối (như bảng màu của Office): đen → sáng 50 / 35 / 25 / 15 / 5%; trắng →
    /// tối 5 / 15 / 25 / 35 / 50%; màu khác → sáng hơn 80 / 60 / 40%, tối hơn 25 / 50%.</summary>
    public static IReadOnlyList<SKColor> Shades(SKColor color)
    {
        var (h, s, l) = ToHslDouble(color);
        double[] lums = l < 0.02 ? [0.50, 0.35, 0.25, 0.15, 0.05]
            : l > 0.98 ? [0.95, 0.85, 0.75, 0.65, 0.50]
            : [l * 0.2 + 0.8, l * 0.4 + 0.6, l * 0.6 + 0.4, l * 0.75, l * 0.5];
        return lums.Select(lum => FromHslDouble(h, s, lum)).ToList();
    }

    /// <summary>Thêm màu vào đầu danh sách Màu tự chọn (mã #RRGGBB): trùng thì chuyển lên đầu, quá
    /// <see cref="MaxCustomColors"/> thì bỏ màu cũ nhất (cuối danh sách).</summary>
    public static List<string> AddCustomColor(IEnumerable<string> colors, SKColor color)
    {
        string hex = ToHtml(color);
        var result = new List<string> { hex };
        foreach (var c in colors)
        {
            if (TryParseHtml(c, out var parsed) && ToHtml(parsed) is var normal && normal != hex && !result.Contains(normal))
            {
                result.Add(normal);
            }
        }
        return result.Take(MaxCustomColors).ToList();
    }
}
