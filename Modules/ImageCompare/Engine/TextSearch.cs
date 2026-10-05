using System.Globalization;
using System.Text;
using SkiaSharp;

namespace ImageCompare.Engine;

/// <summary>1 chỗ tìm thấy: khung bao các từ chứa cụm cần tìm (toạ độ ảnh gốc) + dòng chứa nó.
/// <paramref name="Approximate"/>: khớp gần đúng (OCR đọc sai 1–2 ký tự), không phải khớp chính xác.</summary>
public sealed record TextMatch(int Number, SKRectI Bounds, string LineText, bool Approximate = false);

/// <summary>Tìm 1 chữ / cụm chữ trong kết quả OCR. Tìm trong từng dòng (cụm nhiều từ được), mặc định không
/// phân biệt hoa / thường và không phân biệt dấu ("thanh toan" khớp "Thanh toán") - OCR hay đọc sai 1 dấu,
/// và người tìm hay gõ không dấu. Bỏ qua khoảng trắng khi so: OCR hay dính / tách từ ("Khách hàngC").
///
/// Không thấy chỗ nào khớp chính xác thì tìm gần đúng: cho 1 lỗi (cụm ≥ 7 ký tự: 2) - đọc nhầm, thiếu hoặc thừa 1 ký tự.
/// OCR chữ Nhật trên ảnh chụp màn hình hay nhầm / sót 1 chữ (受注数量 → 受淺数量, 受注金額合計 → 受注金合計). Chỉ khi không bật phân biệt dấu / hoa thường (đã bật = muốn khớp
/// đúng từng ký tự) và cụm ≥ 3 ký tự (ngắn hơn thì gần đúng khớp bừa).</summary>
public static class TextSearch
{
    public static List<TextMatch> Find(OcrResult ocr, string query, bool matchCase, bool matchDiacritics)
    {
        string needle = Normalize(string.Concat(query.Where(c => !char.IsWhiteSpace(c))), matchCase, matchDiacritics);
        if (needle.Length == 0)
        {
            return [];
        }
        var matches = Search(ocr, needle, matchCase, matchDiacritics, maxErrors: 0);
        if (matches.Count == 0 && !matchCase && !matchDiacritics && needle.Length >= 3)
        {
            matches = Search(ocr, needle, matchCase, matchDiacritics, maxErrors: needle.Length >= 7 ? 2 : 1);
        }
        return matches;
    }

    /// <summary>Các chỗ trong từng dòng khớp cụm <paramref name="needle"/> với tối đa <paramref name="maxErrors"/> lỗi: 0 = khớp
    /// chính xác; &gt; 0 = khoảng cách chỉnh sửa (đọc nhầm, thiếu hoặc thừa 1 ký tự - OCR vừa nhầm 注 → 淺, vừa bỏ sót chữ
    /// 受注金額合計 → 受注金合計). Các chỗ không chồng lên nhau.</summary>
    private static List<TextMatch> Search(OcrResult ocr, string needle, bool matchCase, bool matchDiacritics, int maxErrors)
    {
        var matches = new List<TextMatch>();
        foreach (var line in ocr.Lines)
        {
            // Chuỗi dòng đã chuẩn hoá, bỏ khoảng trắng + từ chứa từng ký tự.
            var text = new StringBuilder();
            var owner = new List<int>();
            for (int w = 0; w < line.Words.Count; w++)
            {
                string word = Normalize(line.Words[w].Text, matchCase, matchDiacritics);
                text.Append(word);
                owner.AddRange(Enumerable.Repeat(w, word.Length));
            }
            string haystack = text.ToString();
            var spans = maxErrors == 0 ? ExactSpans(haystack, needle) : ApproximateSpans(haystack, needle, maxErrors);
            foreach (var (start, end) in spans)
            {
                var bounds = line.Words[owner[start]].Bounds;
                for (int w = owner[start] + 1; w <= owner[end - 1]; w++)
                {
                    bounds.Union(line.Words[w].Bounds);
                }
                matches.Add(new TextMatch(matches.Count + 1, bounds, line.Text, maxErrors > 0));
            }
        }
        return matches;
    }

    private static IEnumerable<(int Start, int End)> ExactSpans(string haystack, string needle)
    {
        for (int at = haystack.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            yield return (at, at + needle.Length);
        }
    }

    /// <summary>Đoạn [Start, End) của <paramref name="haystack"/> có khoảng cách chỉnh sửa tới <paramref name="needle"/> ≤
    /// <paramref name="maxErrors"/> (thuật toán Sellers: quy hoạch động như Levenshtein nhưng đoạn khớp bắt đầu ở đâu cũng
    /// được, nhớ vị trí bắt đầu theo từng ô). Nhiều đoạn chồng nhau → lấy đoạn ít lỗi nhất, rồi đoạn dài gần cụm cần tìm.</summary>
    private static List<(int Start, int End)> ApproximateSpans(string haystack, string needle, int maxErrors)
    {
        int m = needle.Length;
        var prev = new int[m + 1];
        var cur = new int[m + 1];
        var prevStart = new int[m + 1];
        var curStart = new int[m + 1];
        for (int i = 0; i <= m; i++)
        {
            prev[i] = i;
        }
        var candidates = new List<(int Start, int End, int Cost)>();
        for (int j = 1; j <= haystack.Length; j++)
        {
            cur[0] = 0;
            curStart[0] = j;
            for (int i = 1; i <= m; i++)
            {
                int substitute = prev[i - 1] + (needle[i - 1] == haystack[j - 1] ? 0 : 1);
                int skipNeedle = cur[i - 1] + 1;   // chữ cần tìm bị OCR bỏ sót
                int extraText = prev[i] + 1;       // OCR đọc thừa 1 chữ
                (cur[i], curStart[i]) = substitute <= skipNeedle && substitute <= extraText ? (substitute, prevStart[i - 1])
                    : skipNeedle <= extraText ? (skipNeedle, curStart[i - 1])
                    : (extraText, prevStart[i]);
            }
            if (cur[m] <= maxErrors && j > curStart[m])
            {
                candidates.Add((curStart[m], j, cur[m]));
            }
            (prev, cur) = (cur, prev);
            (prevStart, curStart) = (curStart, prevStart);
        }
        var chosen = new List<(int Start, int End)>();
        foreach (var c in candidates.OrderBy(c => c.Cost).ThenBy(c => Math.Abs(c.End - c.Start - m)).ThenBy(c => c.Start))
        {
            if (chosen.All(s => c.End <= s.Start || c.Start >= s.End))
            {
                chosen.Add((c.Start, c.End));
            }
        }
        return chosen.OrderBy(s => s.Start).ToList();
    }

    /// <summary>Chuẩn hoá từng ký tự thành đúng 1 ký tự (giữ độ dài chuỗi → vị trí ký tự vẫn khớp với từ gốc).</summary>
    private static string Normalize(string s, bool matchCase, bool matchDiacritics)
    {
        s = s.Normalize(NormalizationForm.FormC);
        var chars = new char[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (!matchDiacritics)
            {
                c = c switch { 'đ' => 'd', 'Đ' => 'D', _ => c.ToString().Normalize(NormalizationForm.FormD)[0] };
            }
            chars[i] = matchCase ? c : char.ToLower(c, CultureInfo.InvariantCulture);
        }
        return new string(chars);
    }
}
