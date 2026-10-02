using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using CsvEditor.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace CsvEditor.Services;

/// <param name="RowsWritten">Số dòng dữ liệu đã ghi (không tính dòng tiêu đề).</param>
/// <param name="SkippedRows">Dòng vượt giới hạn 1.048.576 dòng của Excel - bị bỏ.</param>
/// <param name="SkippedColumns">Cột vượt giới hạn 16.384 cột của Excel - bị bỏ.</param>
/// <param name="TruncatedCells">Ô dài hơn 32.767 ký tự (giới hạn của Excel) - bị cắt.</param>
public sealed record XlsxExportResult(int RowsWritten, int SkippedRows, int SkippedColumns, int TruncatedCells);

/// <summary>Xuất bảng ra 1 sheet .xlsx (Open XML SDK, ghi kiểu streaming nên file lớn không giữ cả sheet trong RAM).
/// Ô trông như số (không có số 0 ở đầu, ≤ 15 chữ số) ghi thành số để Excel tính được; còn lại ghi thành chữ - mã
/// "00123", số điện thoại, "1e5"… giữ nguyên như trong CSV, ô bắt đầu bằng "=" không thành công thức. Dòng tiêu đề
/// in đậm, cố định khi cuộn, có nút lọc.</summary>
public static partial class XlsxExporter
{
    public const int MaxRows = 1_048_576;
    public const int MaxColumns = 16_384;
    public const int MaxCellLength = 32_767;

    /// <summary>Ghi <paramref name="rows"/> ra <paramref name="filePath"/>: ghi ra file tạm rồi mới thay file đích, lỗi
    /// giữa chừng (hết chỗ, huỷ…) không làm hỏng file cũ. <paramref name="columnNames"/> quyết định số cột;
    /// <paramref name="writeHeader"/> = false (file không có dòng tiêu đề) thì không ghi dòng tên cột.</summary>
    public static XlsxExportResult Export(
        string filePath,
        string sheetName,
        IReadOnlyList<string> columnNames,
        bool writeHeader,
        IReadOnlyList<CsvRow> rows,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var columnCount = Math.Min(columnNames.Count, MaxColumns);
        var headerRows = writeHeader ? 1 : 0;
        var rowCount = Math.Min(rows.Count, MaxRows - headerRows);
        var truncated = 0;

        var tempPath = filePath + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            using (var document = SpreadsheetDocument.Create(tempPath, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = CreateStylesheet();
                stylesPart.Stylesheet.Save();

                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var letters = Enumerable.Range(0, columnCount).Select(ColumnName).ToArray();
                var lastColumn = ColumnName(Math.Max(columnCount, 1) - 1);
                var lastRow = (rowCount + headerRows).ToString(CultureInfo.InvariantCulture);
                using (var writer = OpenXmlWriter.Create(worksheetPart))
                {
                    writer.WriteStartElement(new Worksheet());
                    if (writeHeader)
                    {
                        writer.WriteElement(FrozenHeaderView());
                    }
                    if (columnCount > 0)
                    {
                        writer.WriteElement(CreateColumns(columnNames, writeHeader, rows, columnCount));
                    }

                    writer.WriteStartElement(new SheetData());
                    if (writeHeader)
                    {
                        WriteRow(writer, 1, letters, Enumerable.Range(0, columnCount).Select(i => columnNames[i]), styleIndex: 1, ref truncated, numbers: false);
                    }
                    for (var r = 0; r < rowCount; r++)
                    {
                        if (r % 1000 == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            progress?.Report((int)(r * 100L / Math.Max(rowCount, 1)));
                        }
                        var row = rows[r];
                        WriteRow(writer, (uint)(r + 1 + headerRows), letters, Enumerable.Range(0, columnCount).Select(row.GetCell), styleIndex: 0, ref truncated, numbers: true);
                    }
                    writer.WriteEndElement(); // SheetData

                    if (writeHeader && columnCount > 0)
                    {
                        writer.WriteElement(new AutoFilter { Reference = $"A1:{lastColumn}{lastRow}" });
                    }
                    writer.WriteEndElement(); // Worksheet
                }

                var name = SafeSheetName(sheetName);
                var workbook = new Workbook(new Sheets(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 1, Name = name }));
                if (writeHeader && columnCount > 0)
                {
                    // Excel tự tạo tên ẩn này cho vùng lọc; có sẵn thì mở file không phải "sửa" lại.
                    workbook.Append(new DefinedNames(new DefinedName($"'{name.Replace("'", "''")}'!$A$1:${lastColumn}${lastRow}")
                    {
                        Name = "_xlnm._FilterDatabase",
                        LocalSheetId = 0,
                        Hidden = true,
                    }));
                }
                workbookPart.Workbook = workbook;
                workbook.Save();
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, filePath, overwrite: true);
            progress?.Report(100);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        return new XlsxExportResult(rowCount, rows.Count - rowCount, columnNames.Count - columnCount, truncated);
    }

    private static void WriteRow(OpenXmlWriter writer, uint rowIndex, string[] letters, IEnumerable<string> cells, uint styleIndex, ref int truncated, bool numbers)
    {
        writer.WriteStartElement(new Row { RowIndex = rowIndex });
        var column = 0;
        foreach (var raw in cells)
        {
            var reference = letters[column++] + rowIndex.ToString(CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(raw))
            {
                continue;
            }

            var value = CleanText(raw);
            if (value.Length > MaxCellLength)
            {
                value = value[..MaxCellLength];
                truncated++;
            }

            Cell cell;
            if (numbers && IsPlainNumber(value))
            {
                cell = new Cell { CellReference = reference, CellValue = new CellValue(value) };
            }
            else
            {
                var text = new Text(value);
                if (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
                {
                    text.Space = SpaceProcessingModeValues.Preserve;
                }
                cell = new Cell { CellReference = reference, DataType = CellValues.InlineString, InlineString = new InlineString(text) };
            }
            if (styleIndex != 0)
            {
                cell.StyleIndex = styleIndex;
            }
            writer.WriteElement(cell);
        }
        writer.WriteEndElement(); // Row
    }

    /// <summary>Ghi thành số chỉ khi Excel hiện lại y hệt: không số 0 ở đầu ("007"), không dấu +, không dạng mũ / phân
    /// nhóm, ≤ 15 chữ số (Excel chỉ giữ 15 chữ số có nghĩa - mã 16 chữ số sẽ bị làm tròn).</summary>
    internal static bool IsPlainNumber(string value)
    {
        if (!PlainNumberRegex().IsMatch(value))
        {
            return false;
        }
        var digits = value.Count(char.IsAsciiDigit);
        return digits <= 15;
    }

    [GeneratedRegex(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?$")]
    private static partial Regex PlainNumberRegex();

    /// <summary>Bỏ ký tự XML không cho phép (ký tự điều khiển, surrogate lẻ) - còn sót thì Excel báo file hỏng.</summary>
    private static string CleanText(string value)
    {
        StringBuilder? sb = null;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (XmlConvert.IsXmlChar(c))
            {
                sb?.Append(c);
                continue;
            }
            if (i + 1 < value.Length && XmlConvert.IsXmlSurrogatePair(value[i + 1], c))
            {
                sb?.Append(c).Append(value[i + 1]);
                i++;
                continue;
            }
            sb ??= new StringBuilder(value, 0, i, value.Length);
        }
        return sb?.ToString() ?? value;
    }

    /// <summary>Độ rộng cột theo tên cột + 200 dòng đầu, ~1 đơn vị Excel mỗi ký tự, kẹp 8–60.</summary>
    private static Columns CreateColumns(IReadOnlyList<string> columnNames, bool writeHeader, IReadOnlyList<CsvRow> rows, int columnCount)
    {
        var columns = new Columns();
        for (var c = 0; c < columnCount; c++)
        {
            var longest = writeHeader ? columnNames[c].Length + 3 : 0; // + chỗ cho nút lọc
            for (var r = 0; r < Math.Min(rows.Count, 200); r++)
            {
                var cell = rows[r].GetCell(c);
                var firstLine = cell.IndexOfAny(['\r', '\n']) is var nl and >= 0 ? nl : cell.Length;
                longest = Math.Max(longest, firstLine);
            }
            var width = Math.Clamp(longest * 1.1 + 2, 8, 60);
            columns.Append(new Column { Min = (uint)(c + 1), Max = (uint)(c + 1), Width = Math.Round(width, 1), CustomWidth = true });
        }
        return columns;
    }

    private static SheetViews FrozenHeaderView() =>
        new(new SheetView(
            new Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen },
            new Selection { Pane = PaneValues.BottomLeft, ActiveCell = "A2", SequenceOfReferences = new ListValue<StringValue> { InnerText = "A2" } })
        {
            TabSelected = true,
            WorkbookViewId = 0,
        });

    private static Stylesheet CreateStylesheet() =>
        new(
            new Fonts(
                new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
                new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" }))
            { Count = 2 },
            new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }))
            { Count = 2 },
            new Borders(new Border(new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder())) { Count = 1 },
            new CellStyleFormats(new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 }) { Count = 1 },
            new CellFormats(
                new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 },
                new CellFormat { NumberFormatId = 0, FontId = 1, FillId = 0, BorderId = 0, FormatId = 0, ApplyFont = true })
            { Count = 2 },
            new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 });

    /// <summary>Tên sheet hợp lệ: bỏ : \ / ? * [ ], không bắt đầu / kết thúc bằng ', tối đa 31 ký tự, rỗng → "Sheet1".</summary>
    internal static string SafeSheetName(string name)
    {
        var cleaned = new string(name.Where(c => c is not (':' or '\\' or '/' or '?' or '*' or '[' or ']') && !char.IsControl(c)).ToArray()).Trim().Trim('\'');
        if (cleaned.Length > 31)
        {
            cleaned = cleaned[..31].TrimEnd().TrimEnd('\'');
        }
        return cleaned.Length == 0 ? "Sheet1" : cleaned;
    }

    /// <summary>0 → A, 25 → Z, 26 → AA…</summary>
    internal static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name = (char)('A' + ((n - 1) % 26)) + name;
        }
        return name;
    }
}
