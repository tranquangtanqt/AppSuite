using System.Globalization;
using MeRon.Core.Data;

namespace MeRon.Core.Import;

/// <summary>1 bảng đọc từ file TSV, đã kiểm tra và đổi kiểu, sẵn sàng ghi vào SQLite.</summary>
public sealed class TsvTable
{
    public required TableDef Table { get; init; }

    public required string FilePath { get; init; }

    /// <summary>Cột có trong file và có trong schema (cột lạ bị bỏ qua - xem <see cref="Warnings"/>).</summary>
    public required IReadOnlyList<ColumnDef> Columns { get; init; }

    public required IReadOnlyList<object?[]> Rows { get; init; }

    public required IReadOnlyList<string> Errors { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>Giá trị khoá chính của mọi dòng (để kiểm tra liên kết giữa các bảng).</summary>
    public IEnumerable<object> Keys
    {
        get
        {
            var index = IndexOf(Table.PrimaryKey);
            return index < 0 ? [] : Rows.Select(r => r[index]!);
        }
    }

    public int IndexOf(string column)
    {
        for (var i = 0; i < Columns.Count; i++)
        {
            if (string.Equals(Columns[i].Name, column, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>Tối đa bấy nhiêu lỗi mỗi file - file sai định dạng hàng loạt thì vài lỗi đầu là đủ để biết.</summary>
    public const int MaxErrors = 20;

    /// <summary>
    /// Đọc theo đúng định dạng BackupService.exportToLocal của điện thoại: UTF-8, dòng 1 = tên cột, ngăn cách bằng tab,
    /// xuống dòng "\n"; null → ô trống; tab / xuống dòng trong dữ liệu đã bị đổi thành dấu cách nên không cần xử lý
    /// ngoặc kép. Bảng rỗng → file rỗng hẳn (không có dòng tiêu đề).
    /// </summary>
    public static TsvTable Parse(TableDef table, string filePath, string text)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var rows = new List<object?[]>();

        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }
        var lines = text.Split('\n');

        var headerLine = 0;
        while (headerLine < lines.Length && lines[headerLine].TrimEnd('\r').Length == 0)
        {
            headerLine++;
        }
        if (headerLine == lines.Length)
        {
            return new TsvTable { Table = table, FilePath = filePath, Columns = [], Rows = rows, Errors = errors, Warnings = warnings };
        }

        // Cột trong file → vị trí; cột lạ (schema điện thoại mới hơn) bỏ qua; cột thiếu để giá trị mặc định của SQLite.
        var header = lines[headerLine].TrimEnd('\r').Split('\t');
        var columns = new List<ColumnDef>();
        var positions = new List<int>();
        for (var i = 0; i < header.Length; i++)
        {
            var name = header[i].Trim();
            if (table.Find(name) is not { } column)
            {
                warnings.Add($"Bỏ qua cột lạ \"{name}\".");
                continue;
            }
            if (columns.Contains(column))
            {
                errors.Add($"Cột \"{name}\" xuất hiện 2 lần ở dòng tiêu đề.");
                continue;
            }
            columns.Add(column);
            positions.Add(i);
        }
        var missing = table.Columns.Where(c => !columns.Contains(c)).Select(c => c.Name).ToList();
        if (missing.Contains(table.PrimaryKey, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"Dòng tiêu đề thiếu cột khoá \"{table.PrimaryKey}\" - có thể chọn nhầm file. Tiêu đề đang là: {string.Join(", ", header)}");
            return new TsvTable { Table = table, FilePath = filePath, Columns = columns, Rows = rows, Errors = errors, Warnings = warnings };
        }
        if (missing.Count > 0)
        {
            warnings.Add($"File không có cột {string.Join(", ", missing)} - để trống.");
        }

        var keyIndex = columns.FindIndex(c => string.Equals(c.Name, table.PrimaryKey, StringComparison.OrdinalIgnoreCase));
        var seenKeys = new HashSet<object>();
        for (var lineIndex = headerLine + 1; lineIndex < lines.Length && errors.Count < MaxErrors; lineIndex++)
        {
            var line = lines[lineIndex].TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }
            var lineNumber = lineIndex + 1;
            var cells = line.Split('\t');
            if (cells.Length != header.Length)
            {
                errors.Add($"Dòng {lineNumber}: có {cells.Length} ô, dòng tiêu đề có {header.Length} cột.");
                continue;
            }

            var row = new object?[columns.Count];
            var ok = true;
            for (var c = 0; c < columns.Count; c++)
            {
                var cell = cells[positions[c]];
                if (cell.Length == 0)
                {
                    row[c] = null;
                    continue;
                }
                if (columns[c].Kind == ColumnKind.Integer)
                {
                    if (long.TryParse(cell.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
                    {
                        row[c] = number;
                    }
                    else
                    {
                        errors.Add($"Dòng {lineNumber}, cột \"{columns[c].Name}\": \"{cell}\" không phải số nguyên.");
                        ok = false;
                        break;
                    }
                }
                else
                {
                    row[c] = cell;
                }
            }
            if (!ok)
            {
                continue;
            }

            var key = row[keyIndex];
            if (key is null || (key is string s && s.Trim().Length == 0))
            {
                errors.Add($"Dòng {lineNumber}: cột khoá \"{table.PrimaryKey}\" để trống.");
                continue;
            }
            if (!seenKeys.Add(key))
            {
                errors.Add($"Dòng {lineNumber}: trùng khoá \"{table.PrimaryKey}\" = {key}.");
                continue;
            }
            rows.Add(row);
        }
        if (errors.Count >= MaxErrors)
        {
            errors.Add($"... dừng kiểm tra file sau {MaxErrors} lỗi.");
        }

        return new TsvTable { Table = table, FilePath = filePath, Columns = columns, Rows = rows, Errors = errors, Warnings = warnings };
    }
}
