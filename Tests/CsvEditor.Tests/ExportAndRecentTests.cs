using CsvEditor.Models;
using CsvEditor.Services;
using CsvEditor.ViewModels;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;

namespace CsvEditor.Tests;

/// <summary>Xuất .xlsx: đọc lại file bằng Open XML SDK + kiểm tra schema (file sai schema thì Excel báo "hỏng, sửa lại?").</summary>
public sealed class XlsxExportTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static List<CsvRow> Rows(params string[][] rows) => rows.Select(r => new CsvRow(r)).ToList();

    /// <summary>Đọc sheet đầu: (tham chiếu ô → (kiểu, giá trị)), tên sheet, lỗi schema.</summary>
    private static (Dictionary<string, (bool IsNumber, string Value)> Cells, string SheetName, List<string> Errors, Worksheet Sheet) Read(string path)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        var errors = new OpenXmlValidator().Validate(document).Select(e => $"{e.Path?.XPath}: {e.Description}").ToList();
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook.Sheets!.Elements<Sheet>().Single();
        var worksheet = ((WorksheetPart)workbookPart.GetPartById(sheet.Id!)).Worksheet;
        var cells = worksheet.Descendants<Cell>().ToDictionary(
            c => c.CellReference!.Value!,
            c => c.DataType?.Value == CellValues.InlineString ? (false, c.InlineString!.InnerText) : (true, c.CellValue!.Text));
        return (cells, sheet.Name!.Value!, errors, (Worksheet)worksheet.Clone());
    }

    [Fact]
    public void Writes_header_and_rows_keeping_codes_as_text()
    {
        var path = _dir.File("a.xlsx");
        var result = XlsxExporter.Export(path, "khách hàng", ["Mã", "Tên", "Số"],
            writeHeader: true,
            Rows(["00123", "An", "42"], ["7", " Bình ", "-3.50"], ["1e5", "=SUM(A1)", "1234567890123456"]),
            null, CancellationToken.None);

        Assert.Equal(new XlsxExportResult(3, 0, 0, 0), result);
        var (cells, sheetName, errors, sheet) = Read(path);
        Assert.Empty(errors);
        Assert.Equal("khách hàng", sheetName);
        Assert.Equal((false, "Mã"), cells["A1"]);
        Assert.Equal((false, "00123"), cells["A2"]); // số 0 ở đầu: giữ dạng chữ
        Assert.Equal((true, "42"), cells["C2"]);
        Assert.Equal((true, "7"), cells["A3"]);
        Assert.Equal((false, " Bình "), cells["B3"]);
        Assert.Equal((true, "-3.50"), cells["C3"]);
        Assert.Equal((false, "1e5"), cells["A4"]);
        Assert.Equal((false, "=SUM(A1)"), cells["B4"]); // không thành công thức
        Assert.Equal((false, "1234567890123456"), cells["C4"]); // 16 chữ số: Excel sẽ làm tròn → giữ chữ
        Assert.Equal("A1:C4", sheet.GetFirstChild<AutoFilter>()!.Reference!.Value);
        Assert.NotNull(sheet.Descendants<Pane>().SingleOrDefault()); // cố định dòng tiêu đề
    }

    [Fact]
    public void Without_header_option_writes_only_data_and_no_filter()
    {
        var path = _dir.File("n.xlsx");
        XlsxExporter.Export(path, "n", ["Cột 1", "Cột 2"], writeHeader: false, Rows(["1", "x"], ["2", ""]), null, CancellationToken.None);

        var (cells, _, errors, sheet) = Read(path);
        Assert.Empty(errors);
        Assert.Equal((true, "1"), cells["A1"]);
        Assert.Equal((false, "x"), cells["B1"]);
        Assert.False(cells.ContainsKey("B2")); // ô rỗng không ghi
        Assert.Null(sheet.GetFirstChild<AutoFilter>());
        Assert.Empty(sheet.Descendants<Pane>());
    }

    [Fact]
    public void Strips_characters_xml_cannot_hold_and_truncates_overlong_cells()
    {
        var path = _dir.File("c.xlsx");
        var longText = new string('a', XlsxExporter.MaxCellLength + 10);
        var result = XlsxExporter.Export(path, "c", ["A"], writeHeader: true, Rows(["x\u0001y\u0000z😀"], [longText]), null, CancellationToken.None);

        Assert.Equal(1, result.TruncatedCells);
        var (cells, _, errors, _) = Read(path);
        Assert.Empty(errors);
        Assert.Equal((false, "xyz😀"), cells["A2"]);
        Assert.Equal(XlsxExporter.MaxCellLength, cells["A3"].Value.Length);
    }

    [Fact]
    public void Replaces_existing_file_and_leaves_no_temp_file()
    {
        var path = _dir.File("r.xlsx");
        File.WriteAllText(path, "cũ");
        XlsxExporter.Export(path, "r", ["A"], true, Rows(["1"]), null, CancellationToken.None);

        Assert.Empty(Read(path).Errors);
        Assert.Equal(["r.xlsx"], Directory.GetFiles(_dir.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void Cancel_keeps_the_old_file()
    {
        var path = _dir.File("k.xlsx");
        File.WriteAllText(path, "cũ");
        Assert.ThrowsAny<OperationCanceledException>(() =>
            XlsxExporter.Export(path, "k", ["A"], true, Rows(["1"]), null, new CancellationToken(canceled: true)));

        Assert.Equal("cũ", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_dir.Path));
    }

    [Theory]
    [InlineData("data", "data")]
    [InlineData("a[1]:b/c?*", "a1bc")]
    [InlineData("'x'", "x")]
    [InlineData("", "Sheet1")]
    [InlineData("1234567890123456789012345678901234", "1234567890123456789012345678901")]
    public void Sheet_name_is_made_valid(string input, string expected) => Assert.Equal(expected, XlsxExporter.SafeSheetName(input));

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    [InlineData(16_383, "XFD")]
    public void Column_names_follow_excel(int index, string expected) => Assert.Equal(expected, XlsxExporter.ColumnName(index));

    [Fact]
    public async Task ViewModel_exports_only_visible_rows_in_view_order()
    {
        var vm = new CsvEditorViewModel(Csv.FileService(), new CsvEditService());
        var csv = _dir.Write("v.csv", "Tên,Tuổi\nAn,30\nBình,25\nChi,41\n", Csv.Utf8NoBom);
        await vm.OpenAsync(csv, _ => Task.FromResult(true), CancellationToken.None);
        vm.SetQuickFilter(0, "h"); // Bình, Chi
        vm.ApplySort([(1, true)]); // Tuổi giảm dần

        var path = _dir.File("v.xlsx");
        var result = await vm.ExportXlsxAsync(path, CancellationToken.None);

        Assert.Equal(2, result.RowsWritten);
        var (cells, sheetName, errors, _) = Read(path);
        Assert.Empty(errors);
        Assert.Equal("v", sheetName);
        Assert.Equal((false, "Chi"), cells["A2"]);
        Assert.Equal((false, "Bình"), cells["A3"]);
        Assert.Contains("đang lọc: 2/3 dòng", vm.StatusMessage);
        Assert.False(vm.IsDirty);
    }
}

public sealed class TabularClipboardTests
{
    [Fact]
    public void Plain_cells_are_tab_separated_lines()
    {
        Assert.Equal("a\tb\r\n1\t\r\n", TabularClipboard.Format([["a", "b"], ["1", ""]]));
    }

    [Fact]
    public void Cells_with_tab_newline_or_quote_are_quoted_and_round_trip()
    {
        string[][] rows = [["dòng 1\ndòng 2", "a\tb", "nói \"chào\""], ["x", "", "y"]];
        var text = TabularClipboard.Format(rows);

        Assert.Equal("\"dòng 1\ndòng 2\"\t\"a\tb\"\t\"nói \"\"chào\"\"\"\r\nx\t\ty\r\n", text);
        Assert.Equal(rows, TabularClipboard.Parse(text).Select(r => r.ToArray()).ToArray());
    }

    [Fact]
    public void Parses_excel_text_with_lf_or_crlf_and_no_trailing_empty_row()
    {
        Assert.Equal([["a", "b"], ["c", "d"]], TabularClipboard.Parse("a\tb\nc\td\n").Select(r => r.ToArray()));
        Assert.Equal([["a", "b"], ["c", "d"]], TabularClipboard.Parse("a\tb\r\nc\td").Select(r => r.ToArray()));
        Assert.Empty(TabularClipboard.Parse(""));
    }

    [Fact]
    public void Quote_not_wrapping_the_whole_cell_is_kept_as_text()
    {
        Assert.Equal([["\"abc\"def", "x"]], TabularClipboard.Parse("\"abc\"def\tx").Select(r => r.ToArray()));
        Assert.Equal([["5\" inch"]], TabularClipboard.Parse("5\" inch").Select(r => r.ToArray()));
    }
}

public sealed class RecentFilesStoreTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Newest_first_no_duplicates_and_capped()
    {
        var config = _dir.File("Config\\recent.json");
        var store = new RecentFilesStore(config);
        for (var i = 1; i <= 12; i++)
        {
            store.Add(_dir.File($"f{i}.csv"));
        }
        store.Add(_dir.File("F5.CSV")); // trùng f5 (khác hoa thường) → lên đầu, không lặp

        var reloaded = new RecentFilesStore(config).Paths;
        Assert.Equal(RecentFilesStore.MaxCount, reloaded.Count);
        Assert.Equal(_dir.File("F5.CSV"), reloaded[0]);
        Assert.Equal(_dir.File("f12.csv"), reloaded[1]);
        Assert.Single(reloaded, p => p.EndsWith("f5.csv", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_dir.File("f2.csv"), reloaded); // cũ nhất bị đẩy ra
    }

    [Fact]
    public void Remove_and_clear_persist()
    {
        var config = _dir.File("recent.json");
        var store = new RecentFilesStore(config);
        store.Add(_dir.File("a.csv"));
        store.Add(_dir.File("b.csv"));

        store.Remove(_dir.File("A.csv"));
        Assert.Equal([_dir.File("b.csv")], new RecentFilesStore(config).Paths);

        store.Clear();
        Assert.Empty(new RecentFilesStore(config).Paths);
    }

    [Fact]
    public void Corrupt_file_means_empty_list_and_is_overwritten_on_next_add()
    {
        var config = _dir.File("recent.json");
        File.WriteAllText(config, "{ không phải json");
        var store = new RecentFilesStore(config);
        Assert.Empty(store.Paths);

        store.Add(_dir.File("a.csv"));
        Assert.Equal([_dir.File("a.csv")], new RecentFilesStore(config).Paths);
    }
}
