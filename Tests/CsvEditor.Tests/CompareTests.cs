using CsvEditor.Models;
using CsvEditor.Services;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;

namespace CsvEditor.Tests;

/// <summary>So sánh 2 file CSV (Services/CsvCompare, CompareXlsxExporter).</summary>
public class CompareTests
{
    private static readonly string[] NoIgnore = Array.Empty<string>();

    private static CompareOptions ByKey(params string[] keys) => new(keys, NoIgnore);

    private static CompareOptions ByOrder() => new(Array.Empty<string>(), NoIgnore);

    private static CompareResult Run(CsvDocument a, CsvDocument b, CompareOptions options) =>
        CsvCompare.Compare(a.Columns, a.Rows, b.Columns, b.Rows, options);

    // ----- Theo cột khoá -----

    [Fact]
    public void ByKey_FindsModifiedAddedDeletedAndSame()
    {
        var a = Csv.Doc(["Mã", "Tên", "Khu"], ["1", "An", "W1"], ["2", "Bình", "W3"], ["3", "Cường", "505"]);
        var b = Csv.Doc(["Mã", "Tên", "Khu"], ["1", "An", "W1"], ["2", "Bình", "S2"], ["4", "Dung", "W1"]);

        var r = Run(a, b, ByKey("Mã"));

        Assert.Equal((1, 1, 1, 1, 1), (r.Same, r.Modified, r.OnlyB, r.OnlyA, r.ChangedCells));
        var modified = Assert.Single(r.Entries, e => e.Status == CompareStatus.Modified);
        Assert.Equal(("2", "Khu", "W3", "S2", 1, 2), (modified.Key, modified.Column, modified.ValueA, modified.ValueB, modified.RowA, modified.ColumnA));
        var onlyB = Assert.Single(r.Entries, e => e.Status == CompareStatus.OnlyB);
        Assert.Equal(("4", CsvCompare.WholeRecord, -1), (onlyB.Key, onlyB.Column, onlyB.RowA));
        Assert.Equal("Dung · W1", onlyB.ValueB);
        var onlyA = Assert.Single(r.Entries, e => e.Status == CompareStatus.OnlyA);
        Assert.Equal(("3", 2), (onlyA.Key, onlyA.RowA));
    }

    [Fact]
    public void ByKey_ColumnsMatchedByNameEvenWhenReordered_ExtraColumnsReported()
    {
        var a = Csv.Doc(["Mã", "Tên", "Khu", "Cũ"], ["1", "An", "W1", "x"]);
        var b = Csv.Doc(["Khu", "Mã", "Ghi chú", "tên"], ["W1", "1", "mới", "An"]);

        var r = Run(a, b, ByKey("mã"));

        Assert.Equal(1, r.Same);
        Assert.Equal(["Tên", "Khu"], r.ComparedColumns);
        Assert.Equal(["Cũ"], r.ColumnsOnlyA);
        Assert.Equal(["Ghi chú"], r.ColumnsOnlyB);
    }

    [Fact]
    public void ByKey_ModifiedRecordWithManyCells_IsOneGroupInColumnOrderOfA()
    {
        var a = Csv.Doc(["Mã", "Tên", "Khu", "Số dư"], ["1", "An", "W1", "10"]);
        var b = Csv.Doc(["Mã", "Số dư", "Khu", "Tên"], ["1", "20", "W3", "An"]);

        var entries = Run(a, b, ByKey("Mã")).Entries;

        Assert.Equal(["Khu", "Số dư"], entries.Select(e => e.Column));
        Assert.Equal([true, false], entries.Select(e => e.IsGroupStart));
        Assert.All(entries, e => Assert.Equal("1", e.Key));
    }

    [Fact]
    public void ByKey_CompositeKey()
    {
        var a = Csv.Doc(["Mã KH", "Tháng", "Tiền"], ["1", "10", "100"], ["1", "11", "200"], ["2", "10", "50"]);
        var b = Csv.Doc(["Mã KH", "Tháng", "Tiền"], ["1", "11", "250"], ["1", "10", "100"], ["2", "11", "60"]);

        var r = Run(a, b, ByKey("Mã KH", "Tháng"));

        Assert.Equal((1, 1, 1, 1), (r.Same, r.Modified, r.OnlyB, r.OnlyA));
        var modified = Assert.Single(r.Entries, e => e.Status == CompareStatus.Modified);
        Assert.Equal(("1 · 11", "200", "250"), (modified.Key, modified.ValueA, modified.ValueB));
        Assert.Equal("2 · 11", Assert.Single(r.Entries, e => e.Status == CompareStatus.OnlyB).Key);
    }

    [Fact]
    public void ByKey_DuplicateKeysPairedInOrderOfAppearance()
    {
        var a = Csv.Doc(["Mã", "Món"], ["1", "Bánh"], ["1", "Xôi"], ["1", "Chè"]);
        var b = Csv.Doc(["Mã", "Món"], ["1", "Bánh"], ["1", "Khoai"]);

        var r = Run(a, b, ByKey("Mã"));

        Assert.Equal((1, 1, 0, 1), (r.Same, r.Modified, r.OnlyB, r.OnlyA));
        Assert.Equal(("Xôi", "Khoai"), (r.Entries.Single(e => e.Status == CompareStatus.Modified).ValueA, r.Entries.Single(e => e.Status == CompareStatus.Modified).ValueB));
        Assert.Equal((1, 1), (r.DuplicateKeysA, r.DuplicateKeysB));
    }

    [Fact]
    public void ByKey_MissingKeyColumnInB_Throws()
    {
        var a = Csv.Doc(["Mã", "Tên"], ["1", "An"]);
        var b = Csv.Doc(["ID", "Tên"], ["1", "An"]);

        var ex = Assert.Throws<InvalidOperationException>(() => Run(a, b, ByKey("Mã")));
        Assert.Contains("Mã", ex.Message);
    }

    [Fact]
    public void IgnoredColumns_AreNotCompared()
    {
        var a = Csv.Doc(["Mã", "Tên", "Ngày cập nhật"], ["1", "An", "2026-10-01"]);
        var b = Csv.Doc(["Mã", "Tên", "Ngày cập nhật"], ["1", "An", "2026-11-15"]);

        Assert.Equal(1, Run(a, b, ByKey("Mã")).Modified);
        Assert.Equal(1, Run(a, b, new CompareOptions(["Mã"], ["ngày cập nhật"])).Same);
    }

    // ----- Cách so 1 ô -----

    [Theory]
    [InlineData(" An ", "An", true, false, true, true)]
    [InlineData(" An ", "An", false, false, true, false)]
    [InlineData("an", "AN", true, false, true, false)]
    [InlineData("an", "AN", true, true, true, true)]
    [InlineData("1.0", "1", true, false, true, true)]
    [InlineData("1.0", "1", true, false, false, false)]
    [InlineData("50000", "50000.00", true, false, true, true)]
    [InlineData("1,000", "1000", true, false, true, false)] // không đoán dấu phân nhóm
    [InlineData("00123", "123", true, false, true, true)]
    [InlineData("", " ", true, false, true, true)]
    public void Equal_FollowsOptions(string a, string b, bool trim, bool ignoreCase, bool numbers, bool expected)
    {
        var options = new CompareOptions(NoIgnore, NoIgnore, trim, ignoreCase, numbers);
        Assert.Equal(expected, CsvCompare.Equal(a, b, options));
    }

    [Fact]
    public void KeyMatching_UsesSameOptions()
    {
        var a = Csv.Doc(["Mã", "Tên"], ["AB1 ", "An"]);
        var b = Csv.Doc(["Mã", "Tên"], ["ab1", "An"]);

        Assert.Equal(1, Run(a, b, new CompareOptions(["Mã"], NoIgnore, IgnoreCase: true)).Same);
        Assert.Equal((1, 1), (Run(a, b, ByKey("Mã")).OnlyA, Run(a, b, ByKey("Mã")).OnlyB));
    }

    // ----- Không có cột khoá: căn theo thứ tự dòng -----

    [Fact]
    public void ByOrder_InsertedRowDoesNotShiftFollowingRows()
    {
        var a = Csv.Doc(["Tên", "Khu"], ["An", "W1"], ["Bình", "W3"], ["Cường", "505"]);
        var b = Csv.Doc(["Tên", "Khu"], ["An", "W1"], ["Mới", "S2"], ["Bình", "W3"], ["Cường", "505"]);

        var r = Run(a, b, ByOrder());

        Assert.Equal((3, 0, 1, 0), (r.Same, r.Modified, r.OnlyB, r.OnlyA));
        Assert.Equal("Dòng 2 (B)", r.Entries.Single(e => e.Status == CompareStatus.OnlyB).Key);
        Assert.Contains(r.Entries, e => e.Status == CompareStatus.Same && e.Key == "Dòng 3 → 4");
    }

    [Fact]
    public void ByOrder_SimilarRowsBecomeModified_DifferentRowsAreAddedDeleted()
    {
        var a = Csv.Doc(["Tên", "Khu", "Số"], ["An", "W1", "1"], ["Bình", "W3", "2"], ["Cường", "505", "3"], ["Dung", "S2", "4"]);
        var b = Csv.Doc(["Tên", "Khu", "Số"], ["An", "W1", "1"], ["Bình", "W1", "2"], ["Khác hẳn", "X", "9"], ["Dung", "S2", "4"]);

        var r = Run(a, b, ByOrder());

        Assert.Equal((2, 1, 1, 1), (r.Same, r.Modified, r.OnlyB, r.OnlyA));
        var modified = Assert.Single(r.Entries, e => e.Status == CompareStatus.Modified);
        Assert.Equal(("Dòng 2", "Khu", "W3", "W1", 1), (modified.Key, modified.Column, modified.ValueA, modified.ValueB, modified.RowA));
        Assert.Equal("Dòng 3 (A)", r.Entries.Single(e => e.Status == CompareStatus.OnlyA).Key);
        Assert.Equal("Dòng 3 (B)", r.Entries.Single(e => e.Status == CompareStatus.OnlyB).Key);
        Assert.False(r.TooDifferent);
    }

    [Fact]
    public void ByOrder_ResultFollowsFileOrder()
    {
        var a = Csv.Doc(["Tên", "Số"], ["An", "1"], ["Bình", "2"], ["Cường", "3"]);
        var b = Csv.Doc(["Tên", "Số"], ["An", "1"], ["Bình", "5"], ["Cường", "3"]);

        var keys = Run(a, b, ByOrder()).Entries.Select(e => e.Key).ToList();

        Assert.Equal(["Dòng 1", "Dòng 2", "Dòng 3"], keys);
    }

    [Fact]
    public void ByOrder_TooDifferent_FallsBackToExactMatchesOnly()
    {
        var header = new[] { "Mã", "Tên" };
        var rowsA = Enumerable.Range(0, 2600).Select(i => new[] { $"A{i}", "x" });
        var rowsB = Enumerable.Range(0, 2600).Select(i => new[] { $"B{i}", "x" }).Append(["A5", "x"]);
        var a = Csv.Doc([header, .. rowsA]);
        var b = Csv.Doc([header, .. rowsB]);

        var r = Run(a, b, ByOrder());

        Assert.True(r.TooDifferent);
        Assert.Equal((1, 0, 2600, 2599), (r.Same, r.Modified, r.OnlyB, r.OnlyA));
    }

    [Fact]
    public void Preview_UsesColumnOrderOfA_OnBothSides()
    {
        var a = Csv.Doc(["Mã", "Tên", "Khu", "Số dư", "Ghi chú"], ["1", "An", "W1", "10", "a"]);
        var b = Csv.Doc(["Mã", "Khu", "Tên", "Số dư", "Ghi chú"], ["1", "W1", "An", "10", "a"]);

        var same = Assert.Single(Run(a, b, ByKey("Mã")).Entries);

        Assert.Equal("An · W1 · 10 · …", same.ValueA);
        Assert.Equal(same.ValueA, same.ValueB);
    }

    // ----- Gợi ý / kiểm tra cột khoá -----

    [Fact]
    public void SuggestKey_PrefersUniqueIdLikeColumn()
    {
        var doc = Csv.Doc(["Tên", "Mã KH", "Khu"], ["An", "1", "W1"], ["Bình", "2", "W1"], ["Cường", "3", "W3"]);

        Assert.Equal(["Mã KH"], CsvCompare.SuggestKey(doc.Columns, doc.Rows));
    }

    [Fact]
    public void SuggestKey_FindsCompositeKey_WhenNoSingleColumnIsUnique()
    {
        var doc = Csv.Doc(["Mã KH", "Tháng", "Tiền"], ["1", "10", "5"], ["1", "11", "5"], ["2", "10", "6"], ["2", "11", "6"]);

        Assert.Equal(["Mã KH", "Tháng"], CsvCompare.SuggestKey(doc.Columns, doc.Rows));
    }

    [Fact]
    public void SuggestKey_Empty_WhenNothingIsUnique()
    {
        var doc = Csv.Doc(["Tên", "Khu"], ["An", "W1"], ["An", "W1"]);

        Assert.Empty(CsvCompare.SuggestKey(doc.Columns, doc.Rows));
    }

    [Fact]
    public void CheckKey_CountsDuplicatesAndEmptyKeys()
    {
        var doc = Csv.Doc(["Mã", "Tên"], ["1", "An"], ["1", "Bình"], ["1", "Chi"], ["2", "Dung"], ["", "E"]);

        var check = CsvCompare.CheckKey(doc.Columns, doc.Rows, ["Mã"], ByKey("Mã"));

        Assert.False(check.IsUnique);
        Assert.Equal((5, 1, 1, "1", 3), (check.Rows, check.DuplicateValues, check.EmptyKeys, check.Example, check.ExampleCount));
        Assert.True(CsvCompare.CheckKey(doc.Columns, doc.Rows, ["Mã", "Tên"], ByKey("Mã", "Tên")).DuplicateValues == 0);
    }

    // ----- Xuất Excel -----

    [Fact]
    public void ExportXlsx_WritesColoredResultAndSummary()
    {
        using var temp = new TempDir();
        var a = Csv.Doc(["Mã", "Tên", "Khu"], ["1", "An", "W1"], ["2", "Bình", "W3"], ["3", "Cường", "505"]);
        var b = Csv.Doc(["Mã", "Tên", "Khu"], ["1", "An", "W1"], ["2", "Bình", "S2"], ["4", "Dung", "007"]);
        var options = ByKey("Mã");
        var result = Run(a, b, options);
        var shown = result.Entries.Where(e => e.Status != CompareStatus.Same).ToList();
        var path = temp.File("so sánh.xlsx");

        var written = CompareXlsxExporter.Export(path, new CompareSummary("a.csv", 3, "b.csv", 3, options, result, "Sửa, Chỉ có ở B, Chỉ có ở A"), shown, TestContext.Current.CancellationToken);

        Assert.Equal(3, written);
        using var document = SpreadsheetDocument.Open(path, false);
        Assert.Empty(new OpenXmlValidator().Validate(document, TestContext.Current.CancellationToken).Select(e => $"{e.Path?.XPath}: {e.Description}"));
        var sheets = document.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().Select(s => s.Name!.Value).ToList();
        Assert.Equal(["Kết quả", "Tóm tắt"], sheets);

        var resultRows = Rows(document, 0);
        Assert.Equal(["Mã", "Cột", "File A", "File B", "Trạng thái"], resultRows[0].Values);
        Assert.Equal(["2", "Khu", "W3", "S2", "Sửa"], resultRows[1].Values);
        Assert.Equal(["4", CsvCompare.WholeRecord, "", "Dung · 007", "Chỉ có ở B"], resultRows[2].Values);
        Assert.Equal(["3", CsvCompare.WholeRecord, "Cường · 505", "", "Chỉ có ở A"], resultRows[3].Values);
        Assert.Equal([2u, 3u, 4u], resultRows.Skip(1).Select(r => r.Style).ToArray());

        var summary = Rows(document, 1).Where(r => r.Values.Count == 2 && r.Values[0].Length > 0).ToDictionary(r => r.Values[0], r => r.Values[1]);
        Assert.Equal("Theo cột khoá: Mã", summary["Ghép bản ghi"]);
        Assert.Equal("1 bản ghi (1 ô)", summary["Sửa"]);
        Assert.Contains("Sửa, Chỉ có ở B, Chỉ có ở A", summary["Sheet Kết quả"]);
    }

    private static List<(List<string> Values, uint Style)> Rows(SpreadsheetDocument document, int sheetIndex)
    {
        var sheet = document.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().ElementAt(sheetIndex);
        var part = (WorksheetPart)document.WorkbookPart.GetPartById(sheet.Id!);
        return part.Worksheet.Descendants<Row>().Select(row =>
        {
            var cells = row.Elements<Cell>().ToList();
            var values = new List<string>();
            foreach (var cell in cells)
            {
                var column = cell.CellReference!.Value![0] - 'A';
                while (values.Count < column)
                {
                    values.Add("");
                }
                values.Add(cell.InlineString?.InnerText ?? "");
            }
            return (values, cells.LastOrDefault()?.StyleIndex?.Value ?? 0u);
        }).ToList();
    }
}
