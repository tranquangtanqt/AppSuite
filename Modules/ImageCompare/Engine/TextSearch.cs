using System.Globalization;
using System.Text;
using SkiaSharp;

namespace ImageCompare.Engine;

/// <summary>1 chỗ tìm thấy: khung bao các từ chứa cụm cần tìm (toạ độ ảnh gốc) + dòng chứa nó.</summary>
public sealed record TextMatch(int Number, SKRectI Bounds, string LineText);

/// <summary>Tìm 1 chữ / cụm chữ trong kết quả OCR. Tìm trong từng dòng (cụm nhiều từ được), mặc định không
/// phân biệt hoa / thường và không phân biệt dấu ("thanh toan" khớp "Thanh toán") - OCR hay đọc sai 1 dấu,
/// và người tìm hay gõ không dấu. Bỏ qua khoảng trắng khi so: OCR hay dính / tách từ ("Khách hàngC").</summary>
public static class TextSearch
{
    public static List<TextMatch> Find(OcrResult ocr, string query, bool matchCase, bool matchDiacritics)
    {
        var matches = new List<TextMatch>();
        string needle = Normalize(string.Concat(query.Where(c => !char.IsWhiteSpace(c))), matchCase, matchDiacritics);
        if (needle.Length == 0)
        {
            return matches;
        }
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
            for (int at = haystack.IndexOf(needle, StringComparison.Ordinal); at >= 0;
                 at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            {
                var bounds = line.Words[owner[at]].Bounds;
                for (int w = owner[at] + 1; w <= owner[at + needle.Length - 1]; w++)
                {
                    bounds.Union(line.Words[w].Bounds);
                }
                matches.Add(new TextMatch(matches.Count + 1, bounds, line.Text));
            }
        }
        return matches;
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
