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
