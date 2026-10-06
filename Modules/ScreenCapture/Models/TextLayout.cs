using System.Text;
using SkiaSharp;

namespace ScreenCapture.Models;

/// <summary>Phông của chữ trên ảnh (Text, Khung chú thích).</summary>
public readonly record struct TextFont(string Family, float Size, bool Bold, bool Italic);

/// <summary>Chữ trên ảnh có thể là 1 dòng, nhiều dòng hoặc tự xuống dòng theo bề rộng (Khung chú thích).
/// <see cref="Font"/> không có ký tự nào (vd chữ Nhật với Segoe UI) thì ký tự đó lấy từ phông khác của Windows có ký tự
/// đó - SkiaSharp không tự làm việc này, vẽ thẳng sẽ ra ô vuông.</summary>
public sealed class TextLayout
{
    private readonly record struct Run(string Text, SKTypeface Typeface, float Width);
    private readonly record struct Glyph(string Text, SKTypeface Typeface, float Width, bool BreakAfter, bool IsSpace);

    private readonly List<List<Run>> _lines = [];
    private readonly float _size;

    public float Width { get; private set; }
    public float LineHeight { get; private set; }
    /// <summary>Khoảng từ đỉnh dòng tới đường chân chữ (baseline).</summary>
    public float Ascent { get; private set; }
    public float Height => _lines.Count * LineHeight;

    private TextLayout(float size) => _size = size;

    /// <param name="maxWidth">Bề rộng tối đa của 1 dòng - dài hơn thì xuống dòng ở dấu cách (chữ Nhật / Trung: giữa 2
    /// ký tự bất kỳ); vô hạn = chỉ xuống dòng ở Enter.</param>
    public static TextLayout Create(string text, TextFont font, float maxWidth = float.PositiveInfinity)
    {
        float size = Math.Max(1f, font.Size);
        var layout = new TextLayout(size);
        var primary = FontCache.Get(font.Family, font.Bold, font.Italic);
        var used = new HashSet<SKTypeface> { primary };

        foreach (var paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var glyphs = new List<Glyph>();
            foreach (var rune in paragraph.EnumerateRunes())
            {
                var typeface = FontCache.ForCharacter(primary, rune.Value);
                used.Add(typeface);
                string s = rune.ToString();
                bool space = Rune.IsWhiteSpace(rune);
                glyphs.Add(new Glyph(s, typeface, Measure(s, typeface, size), space || IsCjk(rune.Value), space));
            }
            foreach (var line in Wrap(glyphs, maxWidth))
            {
                layout._lines.Add(ToRuns(line, size));
            }
        }

        foreach (var typeface in used)
        {
            using var paint = new SKPaint { Typeface = typeface, TextSize = size };
            layout.LineHeight = Math.Max(layout.LineHeight, paint.FontSpacing);
            layout.Ascent = Math.Max(layout.Ascent, -paint.FontMetrics.Ascent);
        }
        layout.Width = layout._lines.Count == 0 ? 0 : layout._lines.Max(l => l.Sum(r => r.Width));
        return layout;
    }

    /// <summary>Vẽ toàn bộ chữ, góc trên-trái dòng đầu tại (<paramref name="left"/>, <paramref name="top"/>).</summary>
    public void Draw(SKCanvas canvas, float left, float top, SKPaint paint)
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            float x = left, y = top + i * LineHeight + Ascent;
            foreach (var run in _lines[i])
            {
                using var font = new SKFont(run.Typeface, _size);
                canvas.DrawText(run.Text, x, y, font, paint);
                x += run.Width;
            }
        }
    }

    /// <summary>Chia 1 đoạn thành các dòng không dài quá <paramref name="maxWidth"/>: ngắt sau dấu cách / ký tự CJK gần
    /// nhất, không có chỗ ngắt (1 từ dài hơn cả dòng) thì ngắt ngay giữa từ. Dấu cách ở chỗ ngắt bị bỏ.</summary>
    private static List<List<Glyph>> Wrap(List<Glyph> glyphs, float maxWidth)
    {
        var lines = new List<List<Glyph>>();
        var current = new List<Glyph>();
        float width = 0;
        foreach (var glyph in glyphs)
        {
            if (width + glyph.Width > maxWidth && current.Count > 0 && !glyph.IsSpace)
            {
                int breakAt = current.FindLastIndex(g => g.BreakAfter) + 1;
                if (breakAt <= 0)
                {
                    breakAt = current.Count;
                }
                lines.Add(TrimEndSpaces(current.GetRange(0, breakAt)));
                current = current.GetRange(breakAt, current.Count - breakAt);
                width = current.Sum(g => g.Width);
            }
            current.Add(glyph);
            width += glyph.Width;
        }
        lines.Add(current);
        return lines;
    }

    private static List<Glyph> TrimEndSpaces(List<Glyph> line)
    {
        int count = line.Count;
        while (count > 0 && line[count - 1].IsSpace)
        {
            count--;
        }
        return line.GetRange(0, count);
    }

    /// <summary>Gộp các ký tự liền nhau cùng phông thành 1 lần vẽ (đo lại cả cụm cho đúng khoảng cách chữ).</summary>
    private static List<Run> ToRuns(List<Glyph> line, float size)
    {
        var runs = new List<Run>();
        var text = new StringBuilder();
        SKTypeface? typeface = null;
        foreach (var glyph in line)
        {
            if (typeface is not null && !ReferenceEquals(typeface, glyph.Typeface))
            {
                runs.Add(new Run(text.ToString(), typeface, Measure(text.ToString(), typeface, size)));
                text.Clear();
            }
            typeface = glyph.Typeface;
            text.Append(glyph.Text);
        }
        if (typeface is not null && text.Length > 0)
        {
            runs.Add(new Run(text.ToString(), typeface, Measure(text.ToString(), typeface, size)));
        }
        return runs;
    }

    private static float Measure(string text, SKTypeface typeface, float size)
    {
        using var paint = new SKPaint { Typeface = typeface, TextSize = size };
        return paint.MeasureText(text);
    }

    /// <summary>Chữ Hán / Kana / Hangul / dấu câu CJK - xuống dòng được giữa 2 ký tự bất kỳ.</summary>
    private static bool IsCjk(int cp) =>
        cp is >= 0x2E80 and <= 0x9FFF or >= 0xAC00 and <= 0xD7AF or >= 0xF900 and <= 0xFAFF or >= 0xFF00 and <= 0xFFEF
            or >= 0x20000 and <= 0x3FFFF;
}

/// <summary>Typeface dùng chung cho mọi lần vẽ (tạo mới mỗi lần vẽ lại màn hình sẽ tốn bộ nhớ native).</summary>
internal static class FontCache
{
    private static readonly Dictionary<(string Family, bool Bold, bool Italic), SKTypeface> Typefaces = [];
    private static readonly Dictionary<(SKTypeface Primary, int CodePoint), SKTypeface> Fallbacks = [];
    private static readonly object Gate = new();

    public static SKTypeface Get(string family, bool bold, bool italic)
    {
        lock (Gate)
        {
            if (!Typefaces.TryGetValue((family, bold, italic), out var typeface))
            {
                typeface = SKTypeface.FromFamilyName(family,
                    bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                    SKFontStyleWidth.Normal,
                    italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright) ?? SKTypeface.Default;
                Typefaces[(family, bold, italic)] = typeface;
            }
            return typeface;
        }
    }

    /// <summary><paramref name="primary"/> nếu nó có ký tự này, không thì phông khác của Windows có ký tự đó (cùng đậm /
    /// nghiêng), không phông nào có thì vẫn là <paramref name="primary"/>.</summary>
    public static SKTypeface ForCharacter(SKTypeface primary, int codePoint)
    {
        if (codePoint < 0x80 || primary.ContainsGlyph(codePoint))
        {
            return primary;
        }
        lock (Gate)
        {
            if (!Fallbacks.TryGetValue((primary, codePoint), out var typeface))
            {
                typeface = SKFontManager.Default.MatchCharacter(primary.FamilyName, primary.FontStyle, null, codePoint) ?? primary;
                Fallbacks[(primary, codePoint)] = typeface;
            }
            return typeface;
        }
    }

    /// <summary>Tên các phông đã cài (bỏ phông chữ dọc "@..."), sắp theo ABC - cho ô chọn phông.</summary>
    public static IReadOnlyList<string> InstalledFamilies() =>
        SKFontManager.Default.FontFamilies.Where(f => !f.StartsWith('@')).Distinct().Order(StringComparer.CurrentCultureIgnoreCase).ToList();
}
