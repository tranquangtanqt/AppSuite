using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using Rdbms.HtmlGenerator.Models;

namespace Rdbms.HtmlGenerator.Services;

/// <summary>
/// Xuất "テーブル定義書" (Excel định nghĩa bảng, tiêu đề tiếng Nhật - khách Nhật hay yêu cầu) từ 1 lần đọc (file .db) bằng
/// EPPlus 4.5.3.3 (cùng bản Mcf.* đang dùng). Sheet đầu "テーブル一覧" (mỗi bảng 1 dòng, tên bảng link sang sheet của nó),
/// sau đó mỗi bảng / view 1 sheet: thông tin bảng, カラム定義, インデックス, 外部キー, 制約. In: ngang, vừa 1 trang bề rộng.
/// </summary>
public static class TableDefinitionWorkbook
{
    public const string IndexSheetName = "テーブル一覧";

    private const int TableColumns = 8; // A..H
    private static readonly Color HeaderFill = Color.FromArgb(0xD9, 0xF2, 0xDF);
    private static readonly Color LabelFill = Color.FromArgb(0xF2, 0xF2, 0xF2);
    private static readonly Color PrimaryKeyFill = Color.FromArgb(0xFF, 0xF2, 0xCC);
    private static readonly Color LinkColor = Color.FromArgb(0x05, 0x63, 0xC1);

    /// <summary>Ghi file .xlsx tại <paramref name="path"/> (ghi đè). <paramref name="databaseLabel"/> = tên hiện ở đầu mỗi sheet
    /// khi bảng không có nguồn (vd tên file .db). Trả số sheet bảng đã tạo.</summary>
    public static int Write(SchemaSnapshot snapshot, string databaseLabel, DateTime generatedAt, string path)
    {
        var columnsByTable = Group(snapshot.Columns, c => c.TableName);
        var indexesByTable = Group(snapshot.Indexes, i => i.TableName);
        var foreignKeysByTable = Group(snapshot.ForeignKeys, f => f.TableName);
        var constraintsByTable = Group(snapshot.Constraints, c => c.TableName);

        // Bảng trước, view sau; trong mỗi nhóm theo schema rồi tên.
        var tables = snapshot.Tables
            .OrderBy(t => SchemaQualityChecker.IsView(t))
            .ThenBy(t => t.SourceSheet, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.TableName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var sheetNames = AssignSheetNames(tables.Select(t => t.TableName));

        using var package = new ExcelPackage();
        var index = package.Workbook.Worksheets.Add(IndexSheetName);
        var source = tables.Select(t => t.SourceFile).FirstOrDefault(s => !string.IsNullOrEmpty(s)) ?? databaseLabel;
        WriteIndexSheet(index, tables, sheetNames, columnsByTable, source, generatedAt);

        foreach (var table in tables)
        {
            var sheet = package.Workbook.Worksheets.Add(sheetNames[table.TableName]);
            WriteTableSheet(sheet, table, sheetNames,
                (columnsByTable.GetValueOrDefault(table.TableName) ?? []).OrderBy(c => c.OrdinalPosition).ToList(),
                indexesByTable.GetValueOrDefault(table.TableName) ?? [],
                foreignKeysByTable.GetValueOrDefault(table.TableName) ?? [],
                constraintsByTable.GetValueOrDefault(table.TableName) ?? [],
                snapshot.HasExtendedInfo, generatedAt);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Ghi file tạm rồi thay: file đang mở trong Excel thì báo lỗi mà không làm hỏng bản cũ.
        var temp = path + ".tmp";
        package.SaveAs(new FileInfo(temp));
        File.Move(temp, path, overwrite: true);
        return tables.Count;
    }

    private static void WriteIndexSheet(ExcelWorksheet sheet, List<DbTableRecord> tables, Dictionary<string, string> sheetNames,
        Dictionary<string, List<DbColumnRecord>> columnsByTable, string source, DateTime generatedAt)
    {
        var viewCount = tables.Count(SchemaQualityChecker.IsView);
        sheet.Cells[1, 1].Value = IndexSheetName;
        sheet.Cells[1, 1].Style.Font.Size = 14;
        sheet.Cells[1, 1].Style.Font.Bold = true;
        sheet.Cells[2, 1].Value = $"データベース：{source}　　出力日時：{generatedAt:yyyy/MM/dd HH:mm}";
        sheet.Cells[3, 1].Value = $"テーブル：{tables.Count - viewCount}　　ビュー：{viewCount}";

        const int header = 5;
        string[] titles = ["No", "スキーマ", "テーブル名", "種別", "説明", "件数（概算）", "カラム数"];
        for (var c = 0; c < titles.Length; c++)
        {
            sheet.Cells[header, c + 1].Value = titles[c];
        }
        StyleHeader(sheet.Cells[header, 1, header, titles.Length]);

        var row = header;
        foreach (var table in tables)
        {
            row++;
            sheet.Cells[row, 1].Value = row - header;
            sheet.Cells[row, 2].Value = table.SourceSheet;
            Link(sheet.Cells[row, 3], sheetNames[table.TableName], table.TableName);
            sheet.Cells[row, 4].Value = KindLabel(table.Kind);
            sheet.Cells[row, 5].Value = table.Description;
            if (table.EstimatedRows is { } rows)
            {
                sheet.Cells[row, 6].Value = rows;
            }
            sheet.Cells[row, 7].Value = columnsByTable.GetValueOrDefault(table.TableName)?.Count ?? 0;
        }

        if (row > header)
        {
            var body = sheet.Cells[header + 1, 1, row, titles.Length];
            Border(body);
            body.Style.VerticalAlignment = ExcelVerticalAlignment.Top;
            sheet.Cells[header + 1, 5, row, 5].Style.WrapText = true;
            sheet.Cells[header + 1, 6, row, 6].Style.Numberformat.Format = "#,##0";
        }
        sheet.Cells[header, 1, Math.Max(row, header), titles.Length].AutoFilter = true;
        sheet.View.FreezePanes(header + 1, 1);
        SetWidths(sheet, 6, 14, 32, 14, 50, 13, 9);
        PageSetup(sheet, header);
    }

    private static void WriteTableSheet(ExcelWorksheet sheet, DbTableRecord table, Dictionary<string, string> sheetNames,
        List<DbColumnRecord> columns, List<DbIndexRecord> indexes, List<DbForeignKeyRecord> foreignKeys,
        List<DbConstraintRecord> constraints, bool extended, DateTime generatedAt)
    {
        sheet.Cells[1, 1].Value = "テーブル定義書";
        sheet.Cells[1, 1].Style.Font.Size = 14;
        sheet.Cells[1, 1].Style.Font.Bold = true;
        Link(sheet.Cells[1, TableColumns], IndexSheetName, "← テーブル一覧");
        sheet.Cells[1, TableColumns].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;

        var row = 3;
        void Info(string label, object? value, bool wrap = false)
        {
            sheet.Cells[row, 1, row, 2].Merge = true;
            sheet.Cells[row, 1].Value = label;
            sheet.Cells[row, 1, row, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[row, 1, row, 2].Style.Fill.BackgroundColor.SetColor(LabelFill);
            sheet.Cells[row, 1, row, 2].Style.Font.Bold = true;
            var valueCells = sheet.Cells[row, 3, row, TableColumns];
            valueCells.Merge = true;
            valueCells.Value = value;
            valueCells.Style.WrapText = wrap;
            valueCells.Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            if (wrap && value is string text && text.Length > 0)
            {
                // Ô gộp không tự giãn chiều cao - ước lượng theo số dòng / độ dài (bề rộng C..H ≈ 110 ký tự).
                var lines = text.Split('\n').Sum(l => Math.Max(1, (int)Math.Ceiling(l.Length / 100.0)));
                sheet.Row(row).Height = Math.Min(409, 15 * lines);
            }
            Border(sheet.Cells[row, 1, row, TableColumns]);
            row++;
        }

        Info("データベース", table.SourceFile);
        Info("スキーマ", table.SourceSheet);
        Info("テーブル名", table.TableName);
        Info("種別", KindLabel(table.Kind));
        Info("説明", table.Description, wrap: true);
        Info("件数（概算）", table.EstimatedRows is { } rows ? rows.ToString("#,##0") : SchemaQualityChecker.IsView(table) ? "－" : "統計情報なし");
        Info("出力日", generatedAt.ToString("yyyy/MM/dd"));

        // カラム定義
        row = Section(sheet, row + 1, "■ カラム定義",
            [("No", 1, 1), ("カラム名", 2, 2), ("データ型", 3, 3), ("桁数", 4, 4), ("NOT NULL", 5, 5), ("PK", 6, 6), ("既定値", 7, 7), ("説明", 8, 8)]);
        var first = row;
        var number = 0;
        foreach (var column in columns)
        {
            var (type, length) = SplitType(column);
            sheet.Cells[row, 1].Value = ++number;
            sheet.Cells[row, 2].Value = column.ColumnName;
            sheet.Cells[row, 3].Value = type;
            sheet.Cells[row, 4].Value = length;
            sheet.Cells[row, 5].Value = column.Nullable == "N" ? "○" : "";
            sheet.Cells[row, 6].Value = column.Level == 0 ? "○" : "";
            sheet.Cells[row, 7].Value = column.DefaultValue;
            sheet.Cells[row, 8].Value = column.Description;
            if (column.Level == 0)
            {
                sheet.Cells[row, 1, row, TableColumns].Style.Fill.PatternType = ExcelFillStyle.Solid;
                sheet.Cells[row, 1, row, TableColumns].Style.Fill.BackgroundColor.SetColor(PrimaryKeyFill);
            }
            row++;
        }
        row = FinishRows(sheet, first, row, columns.Count == 0);
        if (row > first)
        {
            sheet.Cells[first, 4, row - 1, 6].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            sheet.Cells[first, 7, row - 1, 8].Style.WrapText = true;
        }

        if (extended)
        {
            row = Section(sheet, row + 1, "■ インデックス",
                [("No", 1, 1), ("インデックス名", 2, 2), ("カラム", 3, 4), ("一意", 5, 5), ("PK", 6, 6), ("種類", 7, 7), ("備考", 8, 8)]);
            first = row;
            number = 0;
            foreach (var i in indexes)
            {
                sheet.Cells[row, 1].Value = ++number;
                sheet.Cells[row, 2].Value = i.IndexName;
                MergeValue(sheet, row, 3, 4, i.Columns);
                sheet.Cells[row, 5].Value = i.IsUnique || i.IsPrimaryKey ? "○" : "";
                sheet.Cells[row, 6].Value = i.IsPrimaryKey ? "○" : "";
                sheet.Cells[row, 7].Value = i.IndexType;
                sheet.Cells[row, 8].Value = i.Note;
                row++;
            }
            row = FinishRows(sheet, first, row, indexes.Count == 0);
            if (row > first)
            {
                sheet.Cells[first, 5, row - 1, 6].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            }
        }

        row = Section(sheet, row + 1, "■ 外部キー", [("No", 1, 1), ("カラム", 2, 2), ("参照テーブル", 3, 4), ("参照カラム", 5, 8)]);
        first = row;
        number = 0;
        foreach (var fk in foreignKeys)
        {
            sheet.Cells[row, 1].Value = ++number;
            sheet.Cells[row, 2].Value = fk.LocalColumns.Replace(",", ", ", StringComparison.Ordinal);
            sheet.Cells[row, 3, row, 4].Merge = true;
            if (sheetNames.TryGetValue(fk.ReferencedTable, out var target))
            {
                Link(sheet.Cells[row, 3], target, fk.ReferencedTable);
            }
            else
            {
                sheet.Cells[row, 3].Value = fk.ReferencedTable; // bảng ở schema không đọc
            }
            MergeValue(sheet, row, 5, 8, (string.IsNullOrEmpty(fk.ReferencedColumns) ? fk.LocalColumns : fk.ReferencedColumns)
                .Replace(",", ", ", StringComparison.Ordinal));
            row++;
        }
        row = FinishRows(sheet, first, row, foreignKeys.Count == 0);

        if (extended)
        {
            row = Section(sheet, row + 1, "■ 制約（UNIQUE / CHECK）", [("No", 1, 1), ("制約名", 2, 2), ("種類", 3, 3), ("定義", 4, 8)]);
            first = row;
            number = 0;
            foreach (var c in constraints)
            {
                sheet.Cells[row, 1].Value = ++number;
                sheet.Cells[row, 2].Value = c.ConstraintName;
                sheet.Cells[row, 3].Value = c.ConstraintType;
                MergeValue(sheet, row, 4, 8, c.Definition);
                sheet.Cells[row, 4, row, 8].Style.WrapText = true;
                row++;
            }
            FinishRows(sheet, first, row, constraints.Count == 0);
        }

        SetWidths(sheet, 5, 28, 22, 9, 9, 6, 20, 50);
        PageSetup(sheet, 0);
    }

    /// <summary>Tiêu đề mục + hàng tiêu đề cột (mỗi cột có thể gộp nhiều ô: (chữ, từ cột, tới cột)). Trả hàng dữ liệu đầu tiên.</summary>
    private static int Section(ExcelWorksheet sheet, int row, string title, (string Text, int From, int To)[] headers)
    {
        sheet.Cells[row, 1].Value = title;
        sheet.Cells[row, 1].Style.Font.Bold = true;
        row++;
        foreach (var (text, from, to) in headers)
        {
            if (to > from)
            {
                sheet.Cells[row, from, row, to].Merge = true;
            }
            sheet.Cells[row, from].Value = text;
        }
        StyleHeader(sheet.Cells[row, 1, row, TableColumns]);
        return row + 1;
    }

    /// <summary>Kẻ khung các hàng vừa ghi; không có hàng nào thì ghi 1 hàng "（なし）". Trả hàng kế tiếp.</summary>
    private static int FinishRows(ExcelWorksheet sheet, int first, int row, bool empty)
    {
        if (empty)
        {
            sheet.Cells[row, 1, row, TableColumns].Merge = true;
            sheet.Cells[row, 1].Value = "（なし）";
            sheet.Cells[row, 1].Style.Font.Color.SetColor(Color.Gray);
            row++;
        }
        var body = sheet.Cells[first, 1, row - 1, TableColumns];
        Border(body);
        body.Style.VerticalAlignment = ExcelVerticalAlignment.Top;
        sheet.Cells[first, 1, row - 1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        return row;
    }

    private static void MergeValue(ExcelWorksheet sheet, int row, int from, int to, object? value)
    {
        sheet.Cells[row, from, row, to].Merge = true;
        sheet.Cells[row, from].Value = value;
    }

    private static void StyleHeader(ExcelRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
        range.Style.Fill.BackgroundColor.SetColor(HeaderFill);
        range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        Border(range);
    }

    private static void Border(ExcelRange range)
    {
        var border = range.Style.Border;
        border.Top.Style = border.Bottom.Style = border.Left.Style = border.Right.Style = ExcelBorderStyle.Thin;
    }

    private static void Link(ExcelRange cell, string sheetName, string text)
    {
        cell.Hyperlink = new ExcelHyperLink($"'{sheetName.Replace("'", "''", StringComparison.Ordinal)}'!A1", text);
        cell.Value = text;
        cell.Style.Font.UnderLine = true;
        cell.Style.Font.Color.SetColor(LinkColor);
    }

    private static void SetWidths(ExcelWorksheet sheet, params double[] widths)
    {
        for (var i = 0; i < widths.Length; i++)
        {
            sheet.Column(i + 1).Width = widths[i];
        }
    }

    private static void PageSetup(ExcelWorksheet sheet, int repeatHeaderRow)
    {
        var print = sheet.PrinterSettings;
        print.Orientation = eOrientation.Landscape;
        print.PaperSize = ePaperSize.A4;
        print.FitToPage = true;
        print.FitToWidth = 1;
        print.FitToHeight = 0;
        if (repeatHeaderRow > 0)
        {
            print.RepeatRows = sheet.Cells[$"{repeatHeaderRow}:{repeatHeaderRow}"];
        }
    }

    /// <summary>Tên hiển thị kiểu bảng (theo DbTableRecord.Kind của 4 nguồn).</summary>
    public static string KindLabel(string kind) => kind.ToUpperInvariant() switch
    {
        "BASE TABLE" or "TABLE" => "テーブル",
        "VIEW" => "ビュー",
        "MATERIALIZED VIEW" => "マテリアライズドビュー",
        "SYSTEM VIEW" => "システムビュー",
        "FOREIGN TABLE" => "外部テーブル",
        _ => kind,
    };

    private static readonly Regex TypeWithLength = new(@"^(?<name>[^(]*)\((?<args>[^)]*)\)(?<rest>.*)$", RegexOptions.CultureInvariant);

    /// <summary>"character varying(100)" → ("character varying", "100"); "numeric(12,2)" → ("numeric", "12,2");
    /// "timestamp(3) without time zone" → ("timestamp without time zone", "3"). Length riêng (nếu nguồn có) được ưu tiên.</summary>
    public static (string Type, string Length) SplitType(DbColumnRecord column)
    {
        var match = TypeWithLength.Match(column.DataType);
        if (!match.Success)
        {
            return (column.DataType, column.Length);
        }
        var type = Regex.Replace($"{match.Groups["name"].Value.Trim()} {match.Groups["rest"].Value.Trim()}", @"\s+", " ").Trim();
        var length = string.IsNullOrEmpty(column.Length) ? match.Groups["args"].Value.Replace(" ", "", StringComparison.Ordinal) : column.Length;
        return (type, length);
    }

    /// <summary>Tên sheet: tối đa 31 ký tự, bỏ ký tự Excel cấm (: \ / ? * [ ]), không trùng (không phân biệt hoa thường,
    /// kể cả với "テーブル一覧") - trùng thì cắt bớt và thêm "~2", "~3"...</summary>
    public static Dictionary<string, string> AssignSheetNames(IEnumerable<string> tableNames)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { IndexSheetName };
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tableName in tableNames)
        {
            if (result.ContainsKey(tableName))
            {
                continue;
            }
            var clean = new string(tableName.Select(c => ":\\/?*[]".Contains(c) ? '_' : c).ToArray()).Trim('\'');
            if (clean.Length == 0)
            {
                clean = "table";
            }
            var name = clean.Length > 31 ? clean[..31] : clean;
            for (var n = 2; !used.Add(name); n++)
            {
                var suffix = $"~{n}";
                name = (clean.Length + suffix.Length > 31 ? clean[..(31 - suffix.Length)] : clean) + suffix;
            }
            result[tableName] = name;
        }
        return result;
    }

    private static Dictionary<string, List<T>> Group<T>(IEnumerable<T> items, Func<T, string> key) =>
        items.GroupBy(key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
}
