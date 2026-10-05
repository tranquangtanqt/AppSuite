using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTools.Core;
using FileTools.Services;
using Microsoft.UI.Dispatching;

namespace FileTools.ViewModels;

/// <summary>1 dòng hiển thị của kết quả so sánh (tiêu đề cụm hoặc 1 dòng).</summary>
public sealed record DiffRow(string NumberA, string NumberB, string Mark, string Text, DiffLineKind Kind, bool IsHeader);

/// <summary>Trang "So sánh 2 file": dòng thêm / bớt / sửa kèm ngữ cảnh + báo cáo HTML.</summary>
public sealed partial class CompareViewModel : JobViewModel, IUsesSharedFile
{
    public ObservableCollection<DiffRow> Rows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand))]
    private string _pathA = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand))]
    private string _pathB = string.Empty;

    [ObservableProperty]
    private bool _ignoreCase;

    [ObservableProperty]
    private bool _ignoreWhitespace;

    [ObservableProperty]
    private double _contextLines = 3;

    [ObservableProperty]
    private string _summary = string.Empty;

    [RelayCommand]
    private async Task BrowseA()
    {
        if (await Pickers.PickFileAsync() is { } p)
        {
            PathA = p;
        }
    }

    [RelayCommand]
    private async Task BrowseB()
    {
        if (await Pickers.PickFileAsync() is { } p)
        {
            PathB = p;
        }
    }

    [RelayCommand]
    private void Swap() => (PathA, PathB) = (PathB, PathA);

    partial void OnPathAChanged(string value) => SharedFile.Set(value);

    /// <summary>File đang làm chung → ô A (trừ khi nó đang ở ô B - không so 1 file với chính nó).</summary>
    public void ApplySharedFile(string path)
    {
        if (!IsBusy && !SharedFile.SamePath(path, PathA) && !SharedFile.SamePath(path, PathB))
        {
            PathA = path;
        }
    }

    /// <summary>Kéo-thả: 2 file cùng lúc → A và B; 1 file → ô còn trống (hoặc B).</summary>
    public void AddPaths(IReadOnlyList<string> paths)
    {
        var files = paths.Where(File.Exists).ToList();
        if (files.Count >= 2)
        {
            (PathA, PathB) = (files[0], files[1]);
        }
        else if (files.Count == 1)
        {
            if (string.IsNullOrEmpty(PathA))
            {
                PathA = files[0];
            }
            else
            {
                PathB = files[0];
            }
        }
    }

    private bool CanCompare() => File.Exists(PathA) && File.Exists(PathB);

    [RelayCommand(CanExecute = nameof(CanCompare))]
    private async Task Compare()
    {
        var options = new CompareOptions
        {
            PathA = PathA,
            PathB = PathB,
            IgnoreCase = IgnoreCase,
            IgnoreWhitespace = IgnoreWhitespace,
            ContextLines = (int)Math.Clamp(ContextLines, 0, 50),
            ReportPath = Path.Combine(Shell.TempFolder, $"sosanh_{Path.GetFileNameWithoutExtension(PathA)}_{Path.GetFileNameWithoutExtension(PathB)}_{DateTime.Now:HHmmss}.html"),
        };
        Rows.Clear();
        Summary = string.Empty;
        CompareResult? result = null;
        await RunAsync("So sánh 2 file", (progress, ct) =>
        {
            result = TextDiff.Compare(options, progress, ct);
            return (options.ReportPath, [DescribeResult(result), "Báo cáo HTML → " + options.ReportPath]);
        });
        if (result is null)
        {
            return;
        }
        foreach (var hunk in result.Hunks)
        {
            Rows.Add(new DiffRow("", "", "", result.TooDifferent
                ? (hunk.Lines[0].Kind == DiffLineKind.Removed ? "Dòng chỉ có ở A (không xét thứ tự)" : "Dòng chỉ có ở B (không xét thứ tự)")
                : hunk.Header, DiffLineKind.Context, true));
            foreach (var l in hunk.Lines)
            {
                Rows.Add(new DiffRow(l.LineA?.ToString("N0") ?? "", l.LineB?.ToString("N0") ?? "",
                    l.Kind switch { DiffLineKind.Removed => "−", DiffLineKind.Added => "+", _ => "" }, l.Text, l.Kind, false));
            }
        }
        Summary = DescribeResult(result);
    }

    private static string DescribeResult(CompareResult r) => r.Identical
        ? $"Giống hệt nhau ({r.LinesA:N0} dòng)."
        : $"A {r.LinesA:N0} dòng · B {r.LinesB:N0} dòng · {r.Removed:N0} dòng chỉ có ở A · {r.Added:N0} dòng chỉ có ở B"
          + (r.TooDifferent ? " - khác nhau quá nhiều, so như tập hợp dòng" : $" · {r.ChangeBlocks:N0} chỗ khác")
          + (r.Truncated ? " (chỉ hiện một phần - xem báo cáo)" : string.Empty);
}

/// <summary>Trang "Theo dõi log": hiện N dòng cuối rồi tự thêm dòng mới (đọc mỗi 0,5 s), lọc theo từ khoá, tạm dừng cuộn.</summary>
public sealed partial class TailViewModel : ObservableObject, IUsesSharedFile
{
    private const int MaxRows = 5000;
    private readonly DispatcherQueueTimer _timer;
    private LogTailer? _tailer;
    private TextMatcher? _filter;
    private bool _polling;

    public TailViewModel()
    {
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.Tick += async (_, _) => await PollAsync();
    }

    public ObservableCollection<string> Lines { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string _sourcePath = string.Empty;

    [ObservableProperty]
    private double _lastLines = 200;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _ignoreDiacritics = true;

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStopped))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string _status = "Chưa theo dõi.";

    public bool IsStopped => !IsRunning;

    /// <summary>Có dòng mới được thêm (trang cuộn xuống cuối nếu đang bật tự cuộn).</summary>
    public event EventHandler? LinesAppended;

    [RelayCommand]
    private async Task BrowseSource()
    {
        if (await Pickers.PickFileAsync() is { } p)
        {
            SourcePath = p;
        }
    }

    partial void OnSourcePathChanged(string value) => SharedFile.Set(value);

    /// <summary>Đang theo dõi thì giữ file cũ - không dừng theo dõi giữa chừng.</summary>
    public void ApplySharedFile(string path)
    {
        if (!IsRunning && !SharedFile.SamePath(path, SourcePath))
        {
            SourcePath = path;
        }
    }

    private bool CanStart() => !IsRunning && File.Exists(SourcePath);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task Start()
    {
        try
        {
            _filter = FilterText.Trim().Length == 0 ? null : new TextMatcher(new MatchOptions { Terms = [FilterText.Trim()], IgnoreDiacritics = IgnoreDiacritics });
            _tailer = new LogTailer(SourcePath);
            var tailer = _tailer;
            int n = (int)Math.Clamp(LastLines, 0, MaxRows);
            var first = await Task.Run(() => tailer.Start(n));
            Lines.Clear();
            Append(first);
            IsRunning = true;
            Status = $"Đang theo dõi {Path.GetFileName(SourcePath)} (mỗi 0,5 s)" + (_filter is null ? string.Empty : $" · chỉ hiện dòng chứa \"{FilterText.Trim()}\"");
            _timer.Start();
        }
        catch (Exception ex)
        {
            Status = "Lỗi: " + ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Stop()
    {
        _timer.Stop();
        IsRunning = false;
        Status = $"Đã dừng · {Lines.Count:N0} dòng đang hiện.";
    }

    [RelayCommand]
    private void Clear() => Lines.Clear();

    /// <summary>Lưu các dòng đang hiện ra file tạm rồi mở.</summary>
    [RelayCommand]
    private void SaveShown()
    {
        try
        {
            Directory.CreateDirectory(Shell.TempFolder);
            var path = Path.Combine(Shell.TempFolder, $"{Path.GetFileNameWithoutExtension(SourcePath)}_theodoi_{DateTime.Now:HHmmss}.log");
            File.WriteAllLines(path, Lines, new System.Text.UTF8Encoding(false));
            Shell.Open(path);
        }
        catch (Exception ex)
        {
            Status = "Không lưu được: " + ex.Message;
        }
    }

    private async Task PollAsync()
    {
        if (_polling || _tailer is not { } tailer)
        {
            return;
        }
        _polling = true;
        try
        {
            var batch = await Task.Run(tailer.Poll);
            if (batch.Notice is { } notice)
            {
                Lines.Add($"──── {DateTime.Now:HH:mm:ss} {notice} ────");
            }
            int shown = Append(batch.Lines);
            if (batch.Lines.Count > 0)
            {
                Status = $"Đang theo dõi {Path.GetFileName(SourcePath)} · {DateTime.Now:HH:mm:ss}: +{batch.Lines.Count:N0} dòng" + (shown < batch.Lines.Count ? $" ({shown:N0} khớp bộ lọc)" : string.Empty);
            }
        }
        catch (Exception ex)
        {
            Status = "Lỗi đọc: " + ex.Message;
        }
        finally
        {
            _polling = false;
        }
    }

    private int Append(IEnumerable<string> lines)
    {
        int shown = 0;
        foreach (var line in lines)
        {
            if (_filter is not null && !_filter.IsMatch(line))
            {
                continue;
            }
            Lines.Add(line);
            shown++;
        }
        while (Lines.Count > MaxRows)
        {
            Lines.RemoveAt(0);
        }
        if (shown > 0)
        {
            LinesAppended?.Invoke(this, EventArgs.Empty);
        }
        return shown;
    }
}

/// <summary>Trang "Thay thế hàng loạt": xem trước số chỗ thay từng file rồi mới ghi (giữ encoding / xuống dòng).</summary>
public sealed partial class ReplaceViewModel : BatchViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    private string _find = string.Empty;

    [ObservableProperty]
    private string _replacement = string.Empty;

    [ObservableProperty]
    private bool _isRegex;

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private bool _wholeWord;

    public ReplaceViewModel()
    {
        // "Xem trước" cần cả chữ cần tìm lẫn danh sách file - nhập chữ trước rồi mới thêm file thì nút phải sáng lên.
        Files.CollectionChanged += (_, _) => PreviewCommand.NotifyCanExecuteChanged();
    }

    protected override string Title => "Thay thế trong";

    protected override string Describe(string path) => "bấm \"Xem trước\" để đếm";

    private ReplaceOptions BuildReplace() => new()
    {
        Find = Find,
        Replacement = Replacement,
        IsRegex = IsRegex,
        MatchCase = MatchCase,
        WholeWord = WholeWord,
    };

    protected override RewriteOptions BuildOptions(IReadOnlyList<string> files, string? outputFolder) => new()
    {
        Files = files,
        OutputFolder = outputFolder,
        Encoding = OutputEncoding.SameAsSource,
        Newline = NewlineMode.Keep,
        Transform = BatchReplacer.BuildTransform(BuildReplace()),
    };

    private bool CanPreview() => Files.Count > 0 && Find.Length > 0;

    [RelayCommand(CanExecute = nameof(CanPreview))]
    private async Task Preview()
    {
        var files = Files.ToList();
        var options = BuildReplace();
        IReadOnlyList<ReplacePreview>? previews = null;
        await RunAsync($"Xem trước thay thế trong {files.Count} file", (progress, ct) =>
        {
            previews = BatchReplacer.Preview(files.Select(f => f.FullName).ToList(), options, progress, ct);
            var notes = new List<string> { $"Tổng {previews.Sum(p => p.Matches):N0} chỗ trong {previews.Count(p => p.Matches > 0)} / {previews.Count} file - chưa ghi gì." };
            foreach (var p in previews.Where(p => p.Matches > 0).Take(20))
            {
                notes.Add($"   {Path.GetFileName(p.Path)}: {p.Matches:N0} chỗ / {p.Lines:N0} dòng");
                notes.AddRange(p.Samples.Take(2).Select(s => $"      dòng {s.Line:N0}: {Clip(s.Before)}  →  {Clip(s.After)}"));
            }
            return (null, notes);
        });
        if (previews is null)
        {
            return;
        }
        foreach (var (item, p) in files.Zip(previews))
        {
            item.Detail = p.Error is { } err ? "Lỗi: " + err : p.Matches == 0 ? "không có chỗ nào" : $"{p.Matches:N0} chỗ / {p.Lines:N0} dòng";
        }
    }

    partial void OnFindChanged(string value) => ConvertCommand.NotifyCanExecuteChanged();

    private static string Clip(string s) => s.Length <= 80 ? s : s[..80] + "…";
}

/// <summary>1 file trong 1 nhóm trùng (tích = sẽ chuyển vào Thùng rác).</summary>
public sealed partial class DuplicateFileItem(string path) : ObservableObject
{
    public string Path { get; } = path;
    public string Name { get; } = System.IO.Path.GetFileName(path);
    public string Folder { get; } = System.IO.Path.GetDirectoryName(path) ?? string.Empty;

    [ObservableProperty]
    private bool _isChecked;
}

public sealed class DuplicateGroupItem(DuplicateGroup group)
{
    public string Header { get; } = $"{group.Files.Count} file giống nhau · mỗi file {FileItem.FormatSize(group.Size)} · thừa {FileItem.FormatSize(group.Wasted)}";
    public ObservableCollection<DuplicateFileItem> Files { get; } = new(group.Files.Select(f => new DuplicateFileItem(f)));
}

/// <summary>Trang "Tìm file trùng": gom file cùng nội dung; tích các bản thừa rồi chuyển vào Thùng rác (luôn giữ ≥ 1 bản).</summary>
public sealed partial class DuplicatesViewModel : JobViewModel
{
    public ObservableCollection<DuplicateGroupItem> Groups { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindCommand))]
    private string _folders = string.Empty;

    [ObservableProperty]
    private string _patterns = "*";

    [ObservableProperty]
    private bool _recursive = true;

    [ObservableProperty]
    private double _minSizeKb;

    [ObservableProperty]
    private string _summary = string.Empty;

    /// <summary>Hộp thoại xác nhận (trang gán - cần XamlRoot).</summary>
    public Func<string, Task<bool>>? Confirm { get; set; }

    [RelayCommand]
    private async Task BrowseFolder()
    {
        if (await Pickers.PickFolderAsync() is { } folder)
        {
            Folders = string.IsNullOrWhiteSpace(Folders) ? folder : Folders.TrimEnd(';', ' ') + "; " + folder;
        }
    }

    private IReadOnlyList<string> FolderList() =>
        Folders.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(f => f.Trim('"')).ToList();

    private bool CanFind() => FolderList().Any(Directory.Exists);

    [RelayCommand(CanExecute = nameof(CanFind))]
    private async Task Find()
    {
        var folders = FolderList().Where(Directory.Exists).ToList();
        long minBytes = Math.Max(1, (long)(MinSizeKb * 1024));
        Groups.Clear();
        Summary = string.Empty;
        DuplicateResult? result = null;
        await RunAsync("Tìm file trùng", (progress, ct) =>
        {
            result = DuplicateFinder.Find(folders, Patterns, Recursive, minBytes, progress, ct);
            var notes = new List<string> { $"Quét {result.FilesScanned:N0} file · {result.Groups.Count:N0} nhóm trùng · thừa {FileItem.FormatSize(result.Groups.Sum(g => g.Wasted))}" };
            notes.AddRange(result.Errors.Take(10).Select(e => "   ⚠ " + e));
            return (null, notes);
        });
        if (result is null)
        {
            return;
        }
        foreach (var g in result.Groups)
        {
            Groups.Add(new DuplicateGroupItem(g));
        }
        Summary = result.Groups.Count == 0
            ? $"Không có file trùng trong {result.FilesScanned:N0} file."
            : $"{result.Groups.Count:N0} nhóm trùng · có thể giải phóng {FileItem.FormatSize(result.Groups.Sum(g => g.Wasted))}";
    }

    /// <summary>Tích mọi bản trừ bản đầu tiên (theo tên) của mỗi nhóm.</summary>
    [RelayCommand]
    private void CheckAllButFirst()
    {
        foreach (var g in Groups)
        {
            for (int i = 0; i < g.Files.Count; i++)
            {
                g.Files[i].IsChecked = i > 0;
            }
        }
    }

    [RelayCommand]
    private void UncheckAll()
    {
        foreach (var f in Groups.SelectMany(g => g.Files))
        {
            f.IsChecked = false;
        }
    }

    [RelayCommand]
    private async Task RecycleChecked()
    {
        var chosen = Groups.SelectMany(g => g.Files).Where(f => f.IsChecked).ToList();
        if (chosen.Count == 0)
        {
            AddMessage("Chưa tích file nào.");
            return;
        }
        if (Groups.FirstOrDefault(g => g.Files.All(f => f.IsChecked)) is { } all)
        {
            AddMessage($"✖ Nhóm \"{all.Files[0].Name}\" đang tích hết - phải giữ lại ít nhất 1 bản.");
            return;
        }
        if (Confirm is not null && !await Confirm($"Chuyển {chosen.Count} file đã tích vào Thùng rác? (Khôi phục lại được từ Thùng rác.)"))
        {
            return;
        }
        int ok = 0;
        foreach (var f in chosen)
        {
            try
            {
                RecycleBin.Send(f.Path);
                ok++;
                foreach (var g in Groups)
                {
                    g.Files.Remove(f);
                }
            }
            catch (Exception ex)
            {
                AddMessage($"✖ {f.Path}: {ex.Message}");
            }
        }
        foreach (var g in Groups.Where(g => g.Files.Count < 2).ToList())
        {
            Groups.Remove(g);
        }
        AddMessage($"✔ Đã chuyển {ok} file vào Thùng rác.");
    }
}
