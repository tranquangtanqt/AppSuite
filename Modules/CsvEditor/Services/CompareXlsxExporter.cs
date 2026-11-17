using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace CsvEditor.Services;

/// <summary>Thông tin ghi ở sheet Tóm tắt.</summary>
/// <param name="Shown">Mô tả các dòng đang hiện được xuất (vd "Sửa, Chỉ có ở B, Chỉ có ở A; tìm \"1020\"").</param>
public sealed record CompareSummary(string FileA, int RowsA, string FileB, int RowsB, CompareOptions Options, CompareResult Result, string Shown);

/// <summary>Xuất kết quả So sánh 2 file ra .xlsx: sheet "Kết quả" (Khoá | Cột | File A | File B | Trạng thái, tô màu theo
/// trạng thái như màn hình, tiêu đề cố định + nút lọc) và sheet "Tóm tắt" (2 file, cách so, số lượng, cột chỉ có ở 1 bên).
/// Mọi ô ghi thành chữ (giữ nguyên "00123", "1.0" như trong CSV). Ghi ra file tạm rồi mới thay file đích.</summary>
public static class CompareXlsxExporter
{
    // Style: 0 thường, 1 tiêu đề đậm, 2 Sửa (vàng), 3 Chỉ có ở B (xanh), 4 Chỉ có ở A (đỏ), 5 Giống (xám).
    private const uint Header = 1;

    /// <returns>Số dòng kết quả đã ghi (vượt giới hạn dòng của Excel thì bỏ phần dư).</returns>
    public static int Export(string filePath, CompareSummary summary, IReadOnlyList<CompareEntry> entries, CancellationToken cancellationToken)
    {
        var count = Math.Min(entries.Count, XlsxExporter.MaxRows - 1);
        var tempPath = filePath + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            using (var document = SpreadsheetDocument.Create(tempPath, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = CreateStylesheet();
                stylesPart.Stylesheet.Save();

                var resultPart = workbookPart.AddNewPart<WorksheetPart>();
                var keyHeader = summary.Options.ByOrder ? "Dòng" : string.Join(" + ", summary.Options.KeyColumns);
                var lastRow = (count + 1).ToString(CultureInfo.InvariantCulture);
                using (var writer = OpenXmlWriter.Create(resultPart))
                {
                    writer.WriteStartElement(new Worksheet());
                    writer.WriteElement(new SheetViews(new SheetView(
                        new Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen },
                        new Selection { Pane = PaneValues.BottomLeft, ActiveCell = "A2", SequenceOfReferences = new ListValue<StringValue> { InnerText = "A2" } })
                    { TabSelected = true, WorkbookViewId = 0 }));
                    writer.WriteElement(Widths(18, 22, 40, 40, 14));
                    writer.WriteStartElement(new SheetData());
                    WriteRow(writer, 1, Header, keyHeader, "Cột", "File A", "File B", "Trạng thái");
                    for (var i = 0; i < count; i++)
                    {
                        if ((i & 0x3FF) == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                        var e = entries[i];
                        var (text, style) = e.Status switch
                        {
                            CompareStatus.Modified => ("Sửa", 2u),
                            CompareStatus.OnlyB => ("Chỉ có ở B", 3u),
                            CompareStatus.OnlyA => ("Chỉ có ở A", 4u),
                            _ => ("Giống", 5u),
                        };
                        WriteRow(writer, (uint)(i + 2), style, e.Key, e.Column,
                            e.Status == CompareStatus.OnlyB ? "" : e.ValueA, e.Status == CompareStatus.OnlyA ? "" : e.ValueB, text);
                    }
                    writer.WriteEndElement(); // SheetData
                    writer.WriteElement(new AutoFilter { Reference = $"A1:E{lastRow}" });
                    writer.WriteEndElement(); // Worksheet
                }

                var summaryPart = workbookPart.AddNewPart<WorksheetPart>();
                using (var writer = OpenXmlWriter.Create(summaryPart))
                {
                    writer.WriteStartElement(new Worksheet());
                    writer.WriteElement(Widths(28, 80));
                    writer.WriteStartElement(new SheetData());
                    uint r = 1;
                    foreach (var (name, value) in SummaryRows(summary, count, entries.Count))
                    {
                        WriteRow(writer, r++, 0, name, value);
                    }
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }

                var workbook = new Workbook(new Sheets(
                    new Sheet { Id = workbookPart.GetIdOfPart(resultPart), SheetId = 1, Name = "Kết quả" },
                    new Sheet { Id = workbookPart.GetIdOfPart(summaryPart), SheetId = 2, Name = "Tóm tắt" }));
                workbook.Append(new DefinedNames(new DefinedName($"'Kết quả'!$A$1:$E${lastRow}")
                {
                    Name = "_xlnm._FilterDatabase",
                    LocalSheetId = 0,
                    Hidden = true,
                }));
                workbookPart.Workbook = workbook;
                workbook.Save();
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        return count;
    }

    private static IEnumerable<(string, string)> SummaryRows(CompareSummary s, int written, int total)
    {
        var r = s.Result;
        var o = s.Options;
        yield return ("File A", $"{s.FileA} ({s.RowsA:N0} dòng)");
        yield return ("File B", $"{s.FileB} ({s.RowsB:N0} dòng)");
        yield return ("Ghép bản ghi", o.ByOrder ? "Không có cột khoá - tự căn theo thứ tự dòng" : "Theo cột khoá: " + string.Join(" + ", o.KeyColumns));
        yield return ("Cách so", string.Join("; ", new[]
        {
            o.TrimSpaces ? "bỏ khoảng trắng đầu / cuối" : "giữ khoảng trắng",
            o.IgnoreCase ? "không phân biệt hoa thường" : "phân biệt hoa thường",
            o.NumbersByValue ? "số bằng nhau coi là giống" : "số so như chữ",
        }));
        yield return ("Bỏ qua cột", o.IgnoredColumns.Count == 0 ? "(không)" : string.Join(", ", o.IgnoredColumns));
        yield return ("Sheet Kết quả", $"{total:N0} dòng đang hiện: {s.Shown}");
        yield return ("", "");
        yield return ("Giống", r.Same.ToString("N0"));
        yield return ("Sửa", $"{r.Modified:N0} bản ghi ({r.ChangedCells:N0} ô)");
        yield return ("Chỉ có ở B (thêm)", r.OnlyB.ToString("N0"));
        yield return ("Chỉ có ở A (bớt)", r.OnlyA.ToString("N0"));
        yield return ("", "");
        yield return ("Cột chỉ có ở A", r.ColumnsOnlyA.Count == 0 ? "(không)" : string.Join(", ", r.ColumnsOnlyA));
        yield return ("Cột chỉ có ở B", r.ColumnsOnlyB.Count == 0 ? "(không)" : string.Join(", ", r.ColumnsOnlyB));
        if (r.DuplicateKeysA + r.DuplicateKeysB > 0)
        {
            yield return ("Khoá trùng", $"A {r.DuplicateKeysA:N0}, B {r.DuplicateKeysB:N0} giá trị (ghép theo thứ tự xuất hiện)");
        }
        if (r.TooDifferent)
        {
            yield return ("Lưu ý", "2 file khác nhau quá nhiều để căn theo thứ tự: chỉ ghép dòng giống hệt, không đoán Sửa");
        }
        if (written < total)
        {
            yield return ("Lưu ý", $"Sheet Kết quả chỉ ghi {written:N0} / {total:N0} dòng (giới hạn của Excel)");
        }
    }

    private static Columns Widths(params double[] widths)
    {
        var columns = new Columns();
        for (var i = 0; i < widths.Length; i++)
        {
            columns.Append(new Column { Min = (uint)(i + 1), Max = (uint)(i + 1), Width = widths[i], CustomWidth = true });
        }
        return columns;
    }

    private static void WriteRow(OpenXmlWriter writer, uint rowIndex, uint style, params string[] cells)
    {
        writer.WriteStartElement(new Row { RowIndex = rowIndex });
        for (var c = 0; c < cells.Length; c++)
        {
            var value = XlsxExporter.CleanText(cells[c]);
            if (value.Length > XlsxExporter.MaxCellLength)
            {
                value = value[..XlsxExporter.MaxCellLength];
            }
            var text = new Text(value);
            if (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
            {
                text.Space = SpaceProcessingModeValues.Preserve;
            }
            var cell = new Cell
            {
                CellReference = XlsxExporter.ColumnName(c) + rowIndex.ToString(CultureInfo.InvariantCulture),
                DataType = CellValues.InlineString,
                InlineString = new InlineString(text),
            };
            if (style != 0)
            {
                cell.StyleIndex = style;
            }
            writer.WriteElement(cell);
        }
        writer.WriteEndElement();
    }

    private static Fill Solid(string argb) =>
        new(new PatternFill(new ForegroundColor { Rgb = argb }, new BackgroundColor { Indexed = 64 }) { PatternType = PatternValues.Solid });

    private static Stylesheet CreateStylesheet() =>
        new(
            new Fonts(
                new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
                new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" }))
            { Count = 2 },
            new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                Solid("FFFFF2CC"), // Sửa
                Solid("FFE2EFDA"), // Chỉ có ở B
                Solid("FFFCE4E4"), // Chỉ có ở A
                Solid("FFF2F2F2")) // Giống
            { Count = 6 },
            new Borders(new Border(new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder())) { Count = 1 },
            new CellStyleFormats(new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 }) { Count = 1 },
            new CellFormats(
                new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 },
                new CellFormat { NumberFormatId = 0, FontId = 1, FillId = 0, BorderId = 0, FormatId = 0, ApplyFont = true },
                new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 2, BorderId = 0, FormatId = 0, ApplyFill = true },
                new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 3, BorderId = 0, FormatId = 0, ApplyFill = true },
                new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 4, BorderId = 0, FormatId = 0, ApplyFill = true },
                new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 5, BorderId = 0, FormatId = 0, ApplyFill = true })
            { Count = 6 },
            new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 });
}
