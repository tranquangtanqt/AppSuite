using System.Text;
using CsvEditor.Models;
using CsvEditor.Services;

namespace CsvEditor.Tests;

/// <summary>Bản khôi phục khi app gặp lỗi lúc còn thay đổi chưa lưu: ghi, mở lại đúng dữ liệu / bảng mã, hỏi / bỏ hỏi, dọn.</summary>
public sealed class RecoveryStoreTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 15, 30, 45);

    private static CsvDocument SampleDoc(string filePath, Encoding encoding, char delimiter = ',')
    {
        var document = Csv.Doc(["Mã", "Tên"], ["1", "Nguyễn, \"An\""], ["2", "dòng 1\r\ndòng 2"]);
        document.FilePath = filePath;
        document.Encoding = encoding;
        document.Delimiter = delimiter;
        return document;
    }

    [Fact]
    public async Task Saved_copy_reopens_with_same_cells_and_original_file_untouched()
    {
        using var dir = new TempDir();
        var original = dir.Write("don_hang.csv", "Mã,Tên\r\n1,cũ\r\n", Csv.Utf8Bom);
        var store = new RecoveryStore(dir.File("Recovery"));

        var path = store.Save(SampleDoc(original, Csv.Utf8Bom), Now)!;

        Assert.Equal("don_hang.khoi-phuc-20261110-153045.csv", Path.GetFileName(path));
        Assert.Equal("Mã,Tên\r\n1,cũ\r\n", File.ReadAllText(original)); // file gốc không bị đụng
        var entry = Assert.Single(store.Pending());
        Assert.Equal(original, entry.Info.OriginalPath);
        Assert.Equal((2, 2), (entry.Info.RowCount, entry.Info.ColumnCount));

        var reopened = await Csv.OpenAsync(path, entry.Info.GetEncoding(), entry.Info.Delimiter, entry.Info.HasHeader);
        Assert.Equal(["Mã", "Tên"], Csv.Header(reopened.Document));
        Assert.Equal([["1", "Nguyễn, \"An\""], ["2", "dòng 1\r\ndòng 2"]], Csv.AllRows(reopened.Document));
        Assert.Equal(Csv.Utf8Bom.GetPreamble(), File.ReadAllBytes(path)[..3]); // giữ BOM như file gốc
    }

    [Fact]
    public async Task Keeps_shift_jis_tab_delimiter_and_no_header_mode()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var dir = new TempDir();
        var store = new RecoveryStore(dir.File("Recovery"));
        var document = Csv.Doc(["Cột 1", "Cột 2"], ["得意先", "東京"]);
        document.FilePath = dir.File("data.tsv");
        document.Encoding = Encoding.GetEncoding(932);
        document.Delimiter = '\t';
        document.HasHeader = false;

        var path = store.Save(document, Now)!;
        var info = Assert.Single(store.Pending()).Info;

        Assert.EndsWith(".tsv", path);
        Assert.Equal((932, '\t', false), (info.GetEncoding().CodePage, info.Delimiter, info.HasHeader));
        var reopened = await Csv.OpenAsync(path, info.GetEncoding(), info.Delimiter, info.HasHeader);
        Assert.Equal([["得意先", "東京"]], Csv.AllRows(reopened.Document));
    }

    [Fact]
    public void New_unsaved_table_and_same_second_get_distinct_names()
    {
        using var dir = new TempDir();
        var store = new RecoveryStore(dir.File("Recovery"));
        var document = SampleDoc(string.Empty, Csv.Utf8NoBom);

        var first = store.Save(document, Now)!;
        var second = store.Save(document, Now)!;

        Assert.Equal("bang-moi.khoi-phuc-20261110-153045.csv", Path.GetFileName(first));
        Assert.Equal("bang-moi.khoi-phuc-20261110-153045-2.csv", Path.GetFileName(second));
        Assert.All(store.Pending(), entry => Assert.Null(entry.Info.OriginalPath));
    }

    [Fact]
    public void Empty_table_is_not_saved()
    {
        using var dir = new TempDir();
        var store = new RecoveryStore(dir.File("Recovery"));

        Assert.Null(store.Save(new CsvDocument(), Now));
        Assert.Empty(store.Pending());
    }

    [Fact]
    public void Pending_is_newest_first_and_dismiss_stops_asking()
    {
        using var dir = new TempDir();
        var store = new RecoveryStore(dir.File("Recovery"));
        var older = store.Save(SampleDoc(dir.File("a.csv"), Csv.Utf8NoBom), Now.AddHours(-1))!;
        var newer = store.Save(SampleDoc(dir.File("b.csv"), Csv.Utf8NoBom), Now)!;

        Assert.Equal([newer, older], store.Pending().Select(entry => entry.Path));

        store.Dismiss(store.Pending()[0], deleteFile: false); // đã mở: giữ .csv, thôi hỏi
        store.Dismiss(store.Pending()[0], deleteFile: true);  // xoá

        Assert.Empty(store.Pending());
        Assert.True(File.Exists(newer));
        Assert.False(File.Exists(older));
    }

    [Fact]
    public async Task Opened_recovery_belongs_to_original_file_unsaved_and_save_writes_original()
    {
        using var dir = new TempDir();
        var original = dir.Write("don_hang.csv", "Mã,Tên\r\n1,cũ\r\n", Csv.Utf8NoBom);
        var store = new RecoveryStore(dir.File("Recovery"));
        var edited = Csv.Doc(["Mã", "Tên"], ["1", "mới"]);
        edited.FilePath = original;
        var entry = (path: store.Save(edited, Now)!, info: store.Pending()[0].Info);
        var vm = new ViewModels.CsvEditorViewModel(Csv.FileService(), new CsvEditService());

        await vm.OpenAsync(entry.path, entry.info.GetEncoding(), entry.info.Delimiter, _ => Task.FromResult(true), CancellationToken.None);
        vm.AdoptRecoveredDocument(entry.info.OriginalPath);

        Assert.Equal(original, vm.CurrentFilePath);
        Assert.Equal("don_hang.csv", vm.FileName);
        Assert.True(vm.IsDirty);
        vm.AddRow(0);
        vm.UndoCommand.Execute(null);
        Assert.True(vm.IsDirty); // hoàn tác về đúng lúc mở vẫn là chưa lưu

        await vm.SaveAsync(CancellationToken.None);

        Assert.False(vm.IsDirty);
        Assert.Equal("Mã,Tên\r\n1,mới\r\n", File.ReadAllText(original));
    }

    [Fact]
    public void Broken_info_file_is_skipped()
    {
        using var dir = new TempDir();
        var store = new RecoveryStore(dir.File("Recovery"));
        var path = store.Save(SampleDoc(dir.File("a.csv"), Csv.Utf8NoBom), Now)!;
        File.WriteAllText(path + ".json", "{hỏng");

        Assert.Empty(store.Pending());
    }

    [Fact]
    public void Cleanup_removes_only_old_copies_already_dismissed()
    {
        using var dir = new TempDir();
        var store = new RecoveryStore(dir.File("Recovery"));
        var dismissedOld = store.Save(SampleDoc(dir.File("a.csv"), Csv.Utf8NoBom), Now)!;
        var pendingOld = store.Save(SampleDoc(dir.File("b.csv"), Csv.Utf8NoBom), Now)!;
        var dismissedNew = store.Save(SampleDoc(dir.File("c.csv"), Csv.Utf8NoBom), Now)!;
        foreach (var entry in store.Pending().Where(entry => entry.Path != pendingOld))
        {
            store.Dismiss(entry, deleteFile: false);
        }
        var old = DateTime.Now.AddDays(-(RecoveryStore.KeepDays + 1));
        File.SetLastWriteTime(dismissedOld, old);
        File.SetLastWriteTime(pendingOld, old);

        store.Cleanup(DateTime.Now);

        Assert.False(File.Exists(dismissedOld));
        Assert.True(File.Exists(pendingOld)); // chưa hỏi người dùng thì không xoá dù cũ
        Assert.True(File.Exists(dismissedNew));
    }
}
