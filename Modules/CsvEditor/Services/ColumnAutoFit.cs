namespace CsvEditor.Services;

/// <summary>Giãn cột vừa nội dung (như bấm đúp mép cột trong Excel). Đo bằng TextBlock thật thì chậm, nên chỉ đo vài chục chữ
/// "dài nhất" của cả cột: xếp hạng bằng <see cref="DisplayLength"/> (rẻ, chạy được trên hàng triệu ô) rồi mới đo các ứng viên.</summary>
public static class ColumnAutoFit
{
    public const double MinWidth = 100;
    public const double MaxWidth = 800;

    /// <summary>Lề hai bên chữ trong ô (cùng số với độ rộng tự tính lúc mở file).</summary>
    public const double Padding = 24;

    /// <summary>Số ứng viên đem đo: chữ Nhật / Việt / Latin rộng khác nhau, xếp hạng theo ký tự chỉ gần đúng nên lấy dư.</summary>
    public const int CandidateCount = 50;

    /// <summary>Độ dài ước lượng của dòng dài nhất trong ô: chữ toàn khổ (Hán, kana, chữ Hàn, ký tự full-width) tính 2.</summary>
    public static int DisplayLength(string text)
    {
        var longest = 0;
        var current = 0;
        foreach (var c in text)
        {
            if (c is '\r' or '\n')
            {
                longest = Math.Max(longest, current);
                current = 0;
                continue;
            }

            current += IsWide(c) ? 2 : 1;
        }

        return Math.Max(longest, current);
    }

    private static bool IsWide(char c) =>
        c is >= 'ᄀ' and <= 'ᅟ' // Hangul Jamo
            or >= '⺀' and <= '꓏' // bộ thủ, kana, Hán, Hangul compatibility…
            or >= '가' and <= '힣' // Hangul
            or >= '豈' and <= '﫿' // Hán tương thích
            or >= '︰' and <= '﹏'
            or >= '＀' and <= '｠' // full-width (ｆｕｌｌ, ０-９)
            or >= '￠' and <= '￦';

    /// <summary>Tối đa <paramref name="count"/> chữ khác nhau, dài nhất theo <see cref="DisplayLength"/>; bỏ ô trống.</summary>
    public static IReadOnlyList<string> LongestTexts(IEnumerable<string> values, int count = CandidateCount) =>
        values.Where(value => value.Length > 0)
            .Distinct()
            .Select(value => (Value: value, Length: DisplayLength(value)))
            .OrderByDescending(item => item.Length)
            .Take(count)
            .Select(item => item.Value)
            .ToList();

    /// <summary>Độ rộng cột từ chữ rộng nhất đã đo (px), kẹp trong [<see cref="MinWidth"/>, <see cref="MaxWidth"/>].</summary>
    public static double Fit(double widestText) => Math.Clamp(widestText + Padding, MinWidth, MaxWidth);
}
