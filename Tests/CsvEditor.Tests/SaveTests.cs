using System.Text;
using CsvEditor.Services;

namespace CsvEditor.Tests;

/// <summary>Save / Save As: ghi đúng delimiter + encoding đang dùng, chỉ bọc ngoặc kép khi cần, mở lại ra
/// đúng dữ liệu.</summary>
public sealed class SaveTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Edit_then_save_then_reopen_round_trips()
    {
        var path = _dir.Write("r.csv", "Id,Name\r\n1,An\r\n2,Bình\r\n", Csv.Utf8NoBom);
        var opened = await Csv.OpenAsync(path);
        var edit = new CsvEditService();
        edit.ReplaceDocument(opened.Document);

        edit.SetCell(edit.Document.Rows[1], 1, "Chi");
        edit.AddRow(2);
        edit.SetCell(edit.Document.Rows[2], 0, "3");
        await Csv.FileService().SaveAsync(edit.Document, null, CancellationToken.None);

        var reopened = await Csv.OpenAsync(path);
        Assert.Equal(["Id", "Name"], Csv.Header(reopened.Document));
        Assert.Equal([["1", "An"], ["2", "Chi"], ["3", ""]], Csv.AllRows(reopened.Document));
    }

    [Fact]
    public async Task Quotes_only_fields_that_need_it()
    {
        var document = Csv.Doc(["Id", "Note"], ["1", "plain"], ["2", "a,b"], ["3", "say \"hi\""], ["4", "x\ny"]);
        var path = _dir.File("q.csv");

        await Csv.FileService().SaveAsAsync(document, path, null, CancellationToken.None);

        var text = File.ReadAllText(path);
        Assert.Equal("Id,Note\r\n1,plain\r\n2,\"a,b\"\r\n3,\"say \"\"hi\"\"\"\r\n4,\"x\ny\"\r\n", text);
    }

    [Fact]
    public async Task Keeps_delimiter_and_utf8_bom()
    {
        var path = _dir.Write("s.csv", "A;B\n1;Phở\n", Csv.Utf8Bom);
        var opened = await Csv.OpenAsync(path);

        await Csv.FileService().SaveAsync(opened.Document, null, CancellationToken.None);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal("A;B\r\n1;Phở\r\n", Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public async Task Keeps_utf8_without_bom()
    {
        var path = _dir.Write("n.csv", "A,B\n1,2\n", Csv.Utf8NoBom);
        var opened = await Csv.OpenAsync(path);

        await Csv.FileService().SaveAsync(opened.Document, null, CancellationToken.None);

        Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]);
    }

    [Fact]
    public async Task Keeps_utf16_encoding()
    {
        var path = _dir.Write("u.csv", "A,B\n日本,1\n", Encoding.Unicode);
        var opened = await Csv.OpenAsync(path);

        await Csv.FileService().SaveAsync(opened.Document, null, CancellationToken.None);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal([0xFF, 0xFE], bytes[..2]);
        Assert.Equal("A,B\r\n日本,1\r\n", Encoding.Unicode.GetString(bytes[2..]));
    }

    [Fact]
    public async Task Save_as_writes_new_file_and_updates_path()
    {
        var document = Csv.Doc(["A"], ["1"]);
        var target = _dir.File("new.csv");

        await Csv.FileService().SaveAsAsync(document, target, null, CancellationToken.None);

        Assert.True(File.Exists(target));
        Assert.Equal(target, document.FilePath);
        Assert.Equal("new.csv", document.FileName);
    }

    [Fact]
    public async Task Save_without_path_requires_save_as()
    {
        var document = Csv.Doc(["A"], ["1"]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Csv.FileService().SaveAsync(document, null, CancellationToken.None));
    }

    [Fact]
    public async Task Tsv_round_trip_keeps_tabs()
    {
        var document = Csv.Doc(["A", "B"], ["x y", "1,5"]);
        document.Delimiter = '\t';
        var path = _dir.File("t.tsv");

        await Csv.FileService().SaveAsAsync(document, path, null, CancellationToken.None);
        var reopened = await Csv.OpenAsync(path);

        Assert.Equal('\t', reopened.Document.Delimiter);
        Assert.Equal([["x y", "1,5"]], Csv.AllRows(reopened.Document)); // dấu phẩy trong TSV không bị bọc/tách
    }
}
