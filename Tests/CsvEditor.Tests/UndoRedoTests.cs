using CsvEditor.Services;

namespace CsvEditor.Tests;

/// <summary>Undo / Redo cho mọi thao tác sửa (Command pattern qua CsvEditService + UndoRedoStack).</summary>
public sealed class UndoRedoTests
{
    private static CsvEditService Editor(params string[][] headerThenRows)
    {
        var service = new CsvEditService();
        service.ReplaceDocument(Csv.Doc(headerThenRows));
        return service;
    }

    private static CsvEditService Sample() => Editor(["A", "B", "C"], ["1", "2", "3"], ["4", "5", "6"]);

    /// <summary>Làm <paramref name="action"/>, kiểm tra kết quả; Undo về đúng như ban đầu; Redo lại đúng kết quả.</summary>
    private static void AssertUndoRedo(CsvEditService e, Action action, string[] expectedHeader, string[][] expectedRows)
    {
        var header0 = Csv.Header(e.Document);
        var rows0 = Csv.AllRows(e.Document);

        action();
        Assert.Equal(expectedHeader, Csv.Header(e.Document));
        Assert.Equal(expectedRows, Csv.AllRows(e.Document));

        e.UndoRedo.Undo();
        Assert.Equal(header0, Csv.Header(e.Document));
        Assert.Equal(rows0, Csv.AllRows(e.Document));

        e.UndoRedo.Redo();
        Assert.Equal(expectedHeader, Csv.Header(e.Document));
        Assert.Equal(expectedRows, Csv.AllRows(e.Document));
    }

    [Fact]
    public void Set_cell()
    {
        var e = Sample();
        AssertUndoRedo(e, () => e.SetCell(e.Document.Rows[1], 2, "X"), ["A", "B", "C"], [["1", "2", "3"], ["4", "5", "X"]]);
    }

    [Fact]
    public void Add_row()
    {
        var e = Sample();
        AssertUndoRedo(e, () => e.AddRow(1), ["A", "B", "C"], [["1", "2", "3"], ["", "", ""], ["4", "5", "6"]]);
    }

    [Fact]
    public void Duplicate_row()
    {
        var e = Sample();
        AssertUndoRedo(e, () => e.DuplicateRow(0), ["A", "B", "C"], [["1", "2", "3"], ["1", "2", "3"], ["4", "5", "6"]]);
    }

    [Fact]
    public void Remove_row()
    {
        var e = Sample();
        AssertUndoRedo(e, () => e.RemoveRow(0), ["A", "B", "C"], [["4", "5", "6"]]);
    }

    [Fact]
    public void Add_column()
    {
        var e = Sample();
        AssertUndoRedo(e, () => e.AddColumn(1, "New"), ["A", "New", "B", "C"], [["1", "", "2", "3"], ["4", "", "5", "6"]]);
    }

    [Fact]
    public void Remove_column()
    {
        var e = Sample();
        AssertUndoRedo(e, () => e.RemoveColumn(1), ["A", "C"], [["1", "3"], ["4", "6"]]);
    }

    [Fact]
    public void Rename_column()
    {
        var e = Sample();
        AssertUndoRedo(e, () => e.RenameColumn(0, "Id"), ["Id", "B", "C"], [["1", "2", "3"], ["4", "5", "6"]]);
    }

    [Fact]
    public void Paste_block_is_one_undo_step()
    {
        var e = Sample();
        AssertUndoRedo(e,
            () => e.PasteBlock(e.Document.Rows, 1, [["a", "b"], ["c", "d"]]),
            ["A", "B", "C"], [["1", "a", "b"], ["4", "c", "d"]]);
        Assert.False(e.UndoRedo.CanRedo);
        e.UndoRedo.Undo();
        Assert.False(e.UndoRedo.CanUndo); // cả khối là 1 bước
    }

    [Fact]
    public void Replace_all_is_one_undo_step()
    {
        var e = Sample();
        var rows = e.Document.Rows;
        AssertUndoRedo(e,
            () => e.ReplaceCells([(rows[0], 0, "x"), (rows[1], 2, "y")]),
            ["A", "B", "C"], [["x", "2", "3"], ["4", "5", "y"]]);
        e.UndoRedo.Undo();
        Assert.False(e.UndoRedo.CanUndo);
    }

    [Fact]
    public void Several_steps_undo_in_reverse_order()
    {
        var e = Sample();
        e.SetCell(e.Document.Rows[0], 0, "a");
        e.AddColumn(3, "D");
        e.RemoveRow(1);

        e.UndoRedo.Undo();
        Assert.Equal(2, e.Document.Rows.Count);
        e.UndoRedo.Undo();
        Assert.Equal(["A", "B", "C"], Csv.Header(e.Document));
        e.UndoRedo.Undo();
        Assert.Equal("1", e.Document.Rows[0].GetCell(0));
        Assert.False(e.UndoRedo.CanUndo);
        Assert.Null(e.UndoRedo.Undo()); // Undo khi hết lịch sử: không làm gì
    }

    [Fact]
    public void New_edit_after_undo_clears_redo()
    {
        var e = Sample();
        e.SetCell(e.Document.Rows[0], 0, "a");
        e.UndoRedo.Undo();
        Assert.True(e.UndoRedo.CanRedo);

        e.SetCell(e.Document.Rows[0], 1, "b");

        Assert.False(e.UndoRedo.CanRedo);
    }

    [Fact]
    public void Setting_same_value_adds_no_undo_step()
    {
        var e = Sample();
        e.SetCell(e.Document.Rows[0], 0, "1");
        e.RenameColumn(0, "A");
        Assert.False(e.UndoRedo.CanUndo);
    }

    [Fact]
    public void Dirty_state_follows_save_point()
    {
        var e = Sample();
        Assert.False(e.UndoRedo.IsDirty);

        e.SetCell(e.Document.Rows[0], 0, "a");
        Assert.True(e.UndoRedo.IsDirty);

        e.UndoRedo.MarkClean();                 // Save
        Assert.False(e.UndoRedo.IsDirty);

        e.UndoRedo.Undo();                      // khác bản đã lưu
        Assert.True(e.UndoRedo.IsDirty);
        e.UndoRedo.Redo();                      // về đúng bản đã lưu
        Assert.False(e.UndoRedo.IsDirty);
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(1, "B")]
    [InlineData(2, "C")] // cột cuối
    public void Remove_column_undo_description_names_the_removed_column(int index, string name)
    {
        var e = Sample();
        e.RemoveColumn(index);

        // Tooltip nút Undo đọc mô tả này sau khi cột đã bị xoá.
        Assert.Equal($"Xóa cột '{name}'", e.UndoRedo.UndoDescription);
        e.UndoRedo.Undo();
        Assert.Equal($"Xóa cột '{name}'", e.UndoRedo.RedoDescription);
    }

    [Fact]
    public void Open_new_document_clears_history()
    {
        var e = Sample();
        e.SetCell(e.Document.Rows[0], 0, "a");

        e.ReplaceDocument(Csv.Doc(["X"], ["1"]));

        Assert.False(e.UndoRedo.CanUndo);
        Assert.False(e.UndoRedo.IsDirty);
    }
}
