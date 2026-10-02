using CsvEditor.Services;
using CsvEditor.ViewModels;

namespace CsvEditor.Tests;

/// <summary>Tuỳ chọn "Dòng đầu là tiêu đề" (người dùng chọn, không tự đoán): mở / lưu / mở lại file không có dòng tên cột.</summary>
public sealed class HeaderOptionTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Without_header_every_row_is_data_and_columns_are_named()
    {
        var path = _dir.Write("n.csv", "1,An,30\r\n2,Bình,25\r\n", Csv.Utf8NoBom);

        var result = await Csv.OpenAsync(path, hasHeader: false);

        Assert.False(result.Document.HasHeader);
        Assert.Equal(["Cột 1", "Cột 2", "Cột 3"], Csv.Header(result.Document));
        Assert.Equal([["1", "An", "30"], ["2", "Bình", "25"]], Csv.AllRows(result.Document));
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task Without_header_column_count_is_the_longest_row_and_nothing_is_cut_on_save()
    {
        // Dòng đầu ngắn hơn: nếu coi là tiêu đề thì field thứ 3 của dòng 2 bị cắt khi lưu.
        var path = _dir.Write("r.csv", "1,2\r\n3,4,5\r\n", Csv.Utf8NoBom);

        var result = await Csv.OpenAsync(path, hasHeader: false);
        Assert.Equal(3, result.Document.Columns.Count);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(0, issue.RowIndex);
        Assert.Contains("bảng có 3 cột", issue.Message);

        await Csv.FileService().SaveAsync(result.Document, null, CancellationToken.None);
        var reopened = await Csv.OpenAsync(path, hasHeader: false);
        Assert.Equal([["1", "2", ""], ["3", "4", "5"]], Csv.AllRows(reopened.Document));
    }

    [Fact]
    public async Task Saving_a_headerless_file_does_not_write_column_names()
    {
        var path = _dir.Write("s.csv", "a,b\r\nc,d\r\n", Csv.Utf8NoBom);
        var opened = await Csv.OpenAsync(path, hasHeader: false);
        var edit = new CsvEditService();
        edit.ReplaceDocument(opened.Document);
        edit.SetCell(edit.Document.Rows[1], 1, "x");

        await Csv.FileService().SaveAsync(edit.Document, null, CancellationToken.None);

        Assert.Equal("a,b\r\nc,x\r\n", File.ReadAllText(path));
    }

    [Fact]
    public async Task Reopen_applies_the_new_choice_and_keeps_encoding_and_delimiter()
    {
        var vm = new CsvEditorViewModel(Csv.FileService(), new CsvEditService());
        var path = _dir.Write("v.csv", "Id;Tên\r\n1;An\r\n", Csv.Utf8Bom);
        await vm.OpenAsync(path, _ => Task.FromResult(true), CancellationToken.None);
        Assert.Equal(1, vm.RowCount);

        vm.HasHeader = false;
        await vm.ReopenAsync(vm.CurrentFilePath!, _ => Task.FromResult(true), CancellationToken.None);

        Assert.Equal(2, vm.RowCount);
        Assert.Equal(["Cột 1", "Cột 2"], vm.Columns.Select(c => c.Name));
        Assert.Contains("không có dòng tiêu đề", vm.StatusMessage);
        await vm.SaveAsync(CancellationToken.None);
        Assert.Equal("Id;Tên\r\n1;An\r\n", File.ReadAllText(path)); // giữ delimiter, không thêm dòng "Cột 1;Cột 2"
        Assert.Equal(Csv.Utf8Bom.GetPreamble(), File.ReadAllBytes(path)[..3]);
    }
}

/// <summary>Có dòng tiêu đề mà dòng dữ liệu dài hơn: không được mất field thừa khi lưu.</summary>
public sealed class LongRowTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Rows_longer_than_header_get_extra_columns_and_survive_save()
    {
        var path = _dir.Write("l.csv", "A,B,C\r\n1,2,3\r\n4,5,6,7,8\r\n9,10,11\r\n", Csv.Utf8NoBom);

        var result = await Csv.OpenAsync(path);

        Assert.Equal(["A", "B", "C", "Cột 4", "Cột 5"], Csv.Header(result.Document));
        Assert.Equal(["4", "5", "6", "7", "8"], Csv.Cells(result.Document.Rows[1], 5));
        Assert.Contains(result.Issues, i => i.RowIndex is null && i.Message.Contains("thêm 2 cột"));
        var rowIssue = Assert.Single(result.Issues, i => i.RowIndex is not null); // chỉ dòng dài, không báo các dòng thường
        Assert.Equal(2, rowIssue.RowIndex);

        await Csv.FileService().SaveAsync(result.Document, null, CancellationToken.None);
        Assert.Equal("A,B,C,Cột 4,Cột 5\r\n1,2,3,,\r\n4,5,6,7,8\r\n9,10,11,,\r\n", File.ReadAllText(path));
    }

    [Fact]
    public async Task Extra_column_names_do_not_clash_with_header_names()
    {
        var path = _dir.Write("c.csv", "Cột 2,X\r\n1,2,3\r\n", Csv.Utf8NoBom);
        var result = await Csv.OpenAsync(path);
        Assert.Equal(["Cột 2", "X", "Cột 3"], Csv.Header(result.Document));

        var clash = _dir.Write("d.csv", "A,Cột 3\r\n1,2,3\r\n", Csv.Utf8NoBom);
        Assert.Equal(["A", "Cột 3", "Cột 3 (2)"], Csv.Header((await Csv.OpenAsync(clash)).Document));
    }

    [Fact]
    public void Recomputed_field_count_warning_compares_with_the_most_common_count()
    {
        // 4 cột (đã nới cho 1 dòng dài), phần lớn các dòng 3 field: chỉ báo dòng dài và dòng thiếu.
        var document = Csv.Doc(["A", "B", "C", "Cột 4"], ["1", "2", "3"], ["4", "5", "6", "7"], ["8", "9", "10"], ["11", "12"]);

        var issues = new ValidationService().ValidateFieldCounts(document);

        Assert.Equal([1, 3], issues.Select(i => i.RowIndex!.Value));
        Assert.Contains("phần lớn các dòng có 3 field", issues[0].Message);
    }
}
