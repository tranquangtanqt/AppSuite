using System.ComponentModel;
using CsvEditor.Models;
using CsvEditor.Services;
using CsvEditor.ViewModels;

namespace CsvEditor.Tests;

/// <summary>ViewModel phải tự báo thay đổi cho giao diện (MainWindow không còn gọi Bindings.Update() chạy
/// lại mọi binding): nút Undo/Redo bật/tắt, nút Clear Filter/Clear Sort, nhãn "Chưa lưu".</summary>
public sealed class ViewModelTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private async Task<CsvEditorViewModel> OpenedAsync()
    {
        var vm = new CsvEditorViewModel(Csv.FileService(), new CsvEditService());
        var path = _dir.Write("v.csv", "A,B\n1,2\n3,4\n", Csv.Utf8NoBom);
        await vm.OpenAsync(path, _ => Task.FromResult(true), CancellationToken.None);
        return vm;
    }

    [Fact]
    public async Task Undo_redo_buttons_are_notified_when_history_changes()
    {
        var vm = await OpenedAsync();
        int undoChanged = 0, redoChanged = 0;
        vm.UndoCommand.CanExecuteChanged += (_, _) => undoChanged++;
        vm.RedoCommand.CanExecuteChanged += (_, _) => redoChanged++;
        Assert.False(vm.UndoCommand.CanExecute(null));

        vm.AddRow(0);
        Assert.True(undoChanged > 0, "sửa xong phải báo nút Undo");
        Assert.True(vm.UndoCommand.CanExecute(null));
        Assert.False(vm.RedoCommand.CanExecute(null));

        vm.UndoCommand.Execute(null);
        Assert.True(redoChanged > 0, "Undo xong phải báo nút Redo");
        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.True(vm.RedoCommand.CanExecute(null));
    }

    [Fact]
    public async Task Dirty_flag_is_notified()
    {
        var vm = await OpenedAsync();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.SetCell(0, 0, "x");
        Assert.True(vm.IsDirty);
        Assert.Contains(nameof(vm.IsDirty), changed);

        vm.UndoCommand.Execute(null);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task Opening_a_file_clears_active_filter_and_sort_and_notifies()
    {
        var vm = await OpenedAsync();
        var filter = new FilterExpression();
        filter.Conditions.Add(new FilterCondition { ColumnIndex = 0, Operator = FilterOperator.Equal, Value = "1" });
        vm.ApplyFilter(filter);
        vm.ApplySort([(0, true)]);
        Assert.True(vm.IsFilterActive);
        Assert.True(vm.IsSortActive);

        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        await vm.OpenAsync(_dir.Write("w.csv", "X\n1\n", Csv.Utf8NoBom), _ => Task.FromResult(true), CancellationToken.None);

        Assert.False(vm.IsFilterActive);
        Assert.False(vm.IsSortActive);
        Assert.Contains(nameof(vm.IsFilterActive), changed); // nút Clear Filter tắt
        Assert.Contains(nameof(vm.IsSortActive), changed);   // nút Clear Sort tắt
    }

    [Fact]
    public async Task Issues_count_raises_property_changed_for_warning_header()
    {
        // "Cảnh báo (N)" bind vào ViewModel.Issues.Count - ObservableCollection phải báo Count đổi.
        var vm = await OpenedAsync();
        var countChanged = false;
        ((INotifyPropertyChanged)vm.Issues).PropertyChanged += (_, e) => countChanged |= e.PropertyName == "Count";

        await vm.OpenAsync(_dir.Write("bad.csv", "A,B,C\n1,2\n", Csv.Utf8NoBom), _ => Task.FromResult(true), CancellationToken.None);

        Assert.True(countChanged);
        Assert.NotEmpty(vm.Issues);
    }
}
