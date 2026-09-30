using System.Text;

namespace FileTools.Core;

/// <summary>
/// Đọc theo <b>bản ghi CSV</b>: 1 ô trong ngoặc kép có thể chứa xuống dòng, nên 1 bản ghi có thể trải nhiều dòng.
/// Nối các dòng (giữ nguyên xuống dòng bên trong) cho tới khi số dấu ngoặc kép chẵn. <see cref="TextLine.Ending"/>
/// là xuống dòng của dòng cuối cùng trong bản ghi.
/// </summary>
public sealed class CsvRecordReader(LineReader lines) : IDisposable
{
    public LineReader Lines => lines;

    public bool TryRead(out TextLine record)
    {
        if (!lines.TryRead(out var first))
        {
            record = default;
            return false;
        }
        if (!Csv.HasOddQuotes(first.Text))
        {
            record = first;
            return true;
        }
        var text = new StringBuilder(first.Text);
        var last = first;
        bool open = true;
        while (open && lines.TryRead(out var next))
        {
            text.Append(last.EndingText).Append(next.Text);
            last = next;
            if (Csv.HasOddQuotes(next.Text))
            {
                open = false;
            }
        }
        record = new TextLine(text.ToString(), last.Ending);
        return true;
    }

    public void Dispose() => lines.Dispose();
}

/// <summary>Tách / ghép field CSV (RFC 4180: ngoặc kép bao ô, "" là 1 dấu ngoặc kép) và đoán dấu phân cách
/// (cách chấm điểm chép từ CsvEditor.Services.DelimiterDetector).</summary>
public static class Csv
{
    public static readonly char[] Delimiters = [',', '\t', ';', '|'];

    public static bool IsCsvPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".csv" or ".tsv";

    public static bool HasOddQuotes(string text)
    {
        int count = 0;
        foreach (char c in text)
        {
            if (c == '"')
            {
                count++;
            }
        }
        return (count & 1) == 1;
    }

    public static List<string> Split(string record, char delimiter)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < record.Length; i++)
        {
            char c = record[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < record.Length && record[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        fields.Add(current.ToString());
        return fields;
    }

    public static string Join(IEnumerable<string> fields, char delimiter) =>
        string.Join(delimiter, fields.Select(f => Quote(f, delimiter)));

    public static string Quote(string field, char delimiter) =>
        field.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0 ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;

    public static int CountFields(string record, char delimiter)
    {
        int count = 1;
        bool inQuotes = false;
        foreach (char c in record)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == delimiter && !inQuotes)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>Chọn dấu phân cách cho số cột lớn và ổn định nhất trên các dòng mẫu; file .tsv ưu tiên Tab.</summary>
    public static char DetectDelimiter(IReadOnlyList<string> sampleRecords, string path = "")
    {
        var lines = sampleRecords.Where(l => l.Length > 0).ToList();
        if (lines.Count == 0)
        {
            return ',';
        }
        if (path.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase) && Score(lines, '\t') > 0)
        {
            return '\t';
        }
        var best = Delimiters.Select(d => (Delimiter: d, Score: Score(lines, d))).MaxBy(x => x.Score);
        return best.Score > 0 ? best.Delimiter : ',';
    }

    /// <summary>Đọc tối đa <paramref name="count"/> bản ghi đầu file để đoán dấu phân cách / lấy header.</summary>
    public static List<string> ReadSample(string path, int count = 50)
    {
        using var reader = new CsvRecordReader(LineReader.Open(path));
        var result = new List<string>();
        while (result.Count < count && reader.TryRead(out var record))
        {
            result.Add(record.Text);
        }
        return result;
    }

    private static double Score(List<string> lines, char delimiter)
    {
        var counts = lines.Select(l => CountFields(l, delimiter)).ToList();
        int mode = counts.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
        if (mode <= 1)
        {
            return 0;
        }
        double stable = counts.Count(c => c == mode) / (double)counts.Count;
        return (mode - 1) * stable;
    }

    public static string Describe(char delimiter) => delimiter switch
    {
        ',' => "dấu phẩy (,)",
        '\t' => "Tab",
        ';' => "dấu chấm phẩy (;)",
        '|' => "dấu gạch đứng (|)",
        _ => delimiter.ToString(),
    };
}
