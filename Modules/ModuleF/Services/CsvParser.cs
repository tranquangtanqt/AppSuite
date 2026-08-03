using ModuleF.Models;

namespace ModuleF.Services;

/// <summary>
/// RFC4180-style tokenizer: handles quoted fields (embedded delimiter, embedded newline, "" as an
/// escaped quote) and falls back gracefully on malformed input instead of throwing, since the source
/// file may be hand-edited and imperfect.
/// </summary>
public static class CsvParser
{
    /// <summary>Streams rows out of <paramref name="reader"/> one at a time so the caller can report
    /// progress / observe cancellation between rows without buffering the whole file up front.</summary>
    public static IEnumerable<string[]> ParseRows(TextReader reader, char delimiter, List<ValidationIssue> issues)
    {
        var field = new System.Text.StringBuilder();
        var row = new List<string>();
        var rowIndex = 0;
        var inQuotes = false;
        var sawAnyCharInRow = false;
        int current;

        while ((current = reader.Read()) != -1)
        {
            var c = (char)current;
            sawAnyCharInRow = true;

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (c == '"' && field.Length == 0)
            {
                inQuotes = true;
                continue;
            }

            if (c == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
                continue;
            }

            if (c is '\r' or '\n')
            {
                if (c == '\r' && reader.Peek() == '\n')
                {
                    reader.Read();
                }

                row.Add(field.ToString());
                field.Clear();
                yield return row.ToArray();
                row.Clear();
                rowIndex++;
                sawAnyCharInRow = false;
                continue;
            }

            field.Append(c);
        }

        if (inQuotes)
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Message = $"Dòng {rowIndex + 1}: dấu ngoặc kép không đóng - dữ liệu phần cuối file có thể bị đọc sai.",
                RowIndex = rowIndex,
            });
        }

        if (sawAnyCharInRow || field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            yield return row.ToArray();
        }
    }

    /// <summary>Quote-aware field count for one line (no embedded-newline support) - used by the
    /// delimiter heuristic, which only samples the first few lines and does not need full streaming
    /// correctness.</summary>
    public static int CountFields(string line, char delimiter)
    {
        var count = 1;
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
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
}
