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
    /// <summary>user@host:port/db, kèm schema nếu đã chọn schema riêng (mặc định thì không ghi cho gọn).</summary>
    public string Target => $"{Record.User}@{Record.Target}"
        + (Record.Schema.Length > 0 && !Record.Schema.Contains("mặc định") ? $" · {Record.Schema}" : string.Empty);
    public string Folder => Record.Folder;
    public string FolderName => Path.GetFileName(Record.Folder.TrimEnd('\\', '/'));
    public string Revision => Record.BackupPath.Length > 0 ? $"{Record.Revision} · có sao lưu trước khi chạy".TrimStart(' ', '·') : Record.Revision;
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
    [NotifyCanExecuteChangedFor(nameof(OpenLogCommand), nameof(OpenFolderCommand), nameof(OpenBackupCommand))]
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

    /// <summary>Thư mục script (Chạy), hoặc file dump (Dump / Restore: mở Explorer chọn sẵn file).</summary>
    private bool CanOpenFolder => Selected is not null && (Directory.Exists(Selected.Folder) || File.Exists(Selected.Folder));

    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private void OpenFolder() => Shell.Reveal(Selected!.Folder);

    private bool CanOpenBackup => Selected is { Record.BackupPath.Length: > 0 } s && Directory.Exists(s.Record.BackupPath);

    /// <summary>Thư mục sao lưu trước khi chạy của lần chạy đó.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenBackup))]
    private void OpenBackup() => Shell.Open(Selected!.Record.BackupPath);

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
