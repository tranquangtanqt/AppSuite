using System.Globalization;
using CsvEditor.Models;

namespace CsvEditor.Services;

/// <summary>Số liệu vùng ô đang chọn ở thanh trạng thái, như Excel: Đếm = ô không trống, Tổng / TB chỉ tính ô là số.
/// Đọc số theo <see cref="CsvNumber"/> như Sắp xếp, Thống kê. Cột đang ẩn không tính.</summary>
public readonly record struct RangeStatistics(int Count, int NumericCount, double Sum)
{
    public double Average => NumericCount > 0 ? Sum / NumericCount : 0;

    public static RangeStatistics Compute(IReadOnlyList<CsvRow> rows, CellRange range, IReadOnlySet<int> hiddenColumns)
    {
        var count = 0;
        var numericCount = 0;
        var sum = 0d;
        for (var r = range.Top; r <= range.Bottom && r < rows.Count; r++)
        {
            for (var c = range.Left; c <= range.Right; c++)
            {
                if (hiddenColumns.Contains(c))
                {
                    continue;
                }

                var cell = rows[r].GetCell(c);
                if (cell.Length == 0)
                {
                    continue;
                }

                count++;
                if (CsvNumber.TryParse(cell, out var value))
                {
                    numericCount++;
                    sum += value;
                }
            }
        }

        return new RangeStatistics(count, numericCount, sum);
    }

    public string Describe(IFormatProvider? provider = null)
    {
        provider ??= CultureInfo.CurrentCulture;
        var text = "Đếm: " + Count.ToString("#,##0", provider);
        return NumericCount == 0
            ? text
            : $"{text}  ·  Tổng: {Sum.ToString("#,##0.##", provider)}  ·  TB: {Average.ToString("#,##0.##", provider)}";
    }
}
