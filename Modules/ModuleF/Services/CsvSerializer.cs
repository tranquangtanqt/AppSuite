using System.Text;
using ModuleF.Models;

namespace ModuleF.Services;

public static class CsvSerializer
{
    /// <summary>Writes header + every row to <paramref name="writer"/>, quoting a field only when it
    /// contains the delimiter, a quote, or a newline (minimal-quoting, matches how most CSV tools
    /// round-trip a file so a re-opened file doesn't gain quotes it didn't have before).</summary>
    public static async Task WriteAsync(TextWriter writer, CsvDocument document, CancellationToken cancellationToken)
    {
        await WriteRowAsync(writer, document.Columns.Select(c => c.Name), document.Delimiter, cancellationToken);

        foreach (var row in document.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cells = Enumerable.Range(0, document.Columns.Count).Select(row.GetCell);
            await WriteRowAsync(writer, cells, document.Delimiter, cancellationToken);
        }
    }

    private static async Task WriteRowAsync(TextWriter writer, IEnumerable<string> cells, char delimiter, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var cell in cells)
        {
            if (!first)
            {
                sb.Append(delimiter);
            }

            first = false;
            sb.Append(QuoteIfNeeded(cell, delimiter));
        }

        sb.Append('\r').Append('\n');
        await writer.WriteAsync(sb, cancellationToken);
    }

    private static string QuoteIfNeeded(string value, char delimiter)
    {
        var needsQuoting = value.Contains(delimiter) || value.Contains('"') || value.Contains('\r') || value.Contains('\n');
        if (!needsQuoting)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
