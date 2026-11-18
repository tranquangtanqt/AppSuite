using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DbTools.Services;

namespace DbTools.ViewModels;

/// <summary>1 dòng lịch sử (hiển thị).</summary>
public sealed class HistoryRow(RunRecord record)
{
    public RunRecord Record { get; } = record;
    public string Time => Record.Time.ToString("yyyy-MM-dd HH:mm");
    public string Mode => Record.Mode + (Record.DropFirst ? " (xoá rồi tạo)" : string.Empty);
    public string Target => $"{Record.User}@{Record.Target}";
    public string Folder => Record.Folder;
    public string FolderName => Path.GetFileName(Record.Folder.TrimEnd('\\', '/'));
    public string Revision => Record.Revision;
    public bool Success => Record.Success;
    public string Result => Record.Success
        ? $"Đạt · {Record.FilesDone}/{Record.FileCount} file · {Record.DurationMs / 1000.0:0.0}s"
        : $"Thất bại · {Record.Error}";
}

/// <summary>Trang "Lịch sử chạy": mọi lần Chạy / Chạy thử, mới nhất ở trên; mở log từng lần.</summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    public ObservableCollection<HistoryRow> Rows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLogCommand), nameof(OpenFolderCommand))]
    private HistoryRow? _selected;

    [ObservableProperty]
    private string _summary = string.Empty;

    /// <summary>Trang gắn vào: hỏi lại trước khi xoá lịch sử.</summary>
    public Func<string, Task<bool>>? ConfirmClear { get; set; }

    public bool IsEmpty => Rows.Count == 0;

    [RelayCommand]
    public void Refresh()
    {
        Rows.Clear();
        foreach (var r in AppState.History.Load())
        {
            Rows.Add(new HistoryRow(r));
        }
        Summary = Rows.Count == 0
            ? "Chưa có lần chạy nào."
            : $"{Rows.Count} lần chạy · gần nhất {Rows[0].Time} ({Rows[0].Mode}, {(Rows[0].Success ? "đạt" : "thất bại")})";
        OnPropertyChanged(nameof(IsEmpty));
        ClearCommand.NotifyCanExecuteChanged();
    }

    private bool CanOpenLog => Selected is { Record.LogPath.Length: > 0 } s && File.Exists(s.Record.LogPath);

    [RelayCommand(CanExecute = nameof(CanOpenLog))]
    private void OpenLog() => Shell.Open(Selected!.Record.LogPath);

    private bool CanOpenFolder => Selected is not null && Directory.Exists(Selected.Folder);

    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private void OpenFolder() => Shell.Open(Selected!.Folder);

    private bool CanClear => Rows.Count > 0;

    [RelayCommand(CanExecute = nameof(CanClear))]
    private async Task ClearAsync()
    {
        if (ConfirmClear is not null && !await ConfirmClear($"Xoá toàn bộ {Rows.Count} dòng lịch sử chạy và file log của chúng?"))
        {
            return;
        }
        AppState.History.Clear();
        Refresh();
    }
}
