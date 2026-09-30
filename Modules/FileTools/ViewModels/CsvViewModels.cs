using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTools.Core;
using FileTools.Services;

namespace FileTools.ViewModels;

/// <summary>1 cột trong danh sách cột (có ô tích để chọn).</summary>
public sealed partial class CsvColumnItem(int index, string name) : ObservableObject
{
    public int Index { get; } = index;
    public string Name { get; } = name;
    public string Label => $"{Index + 1}. {Name}";

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>
/// Nền cho các trang CSV: chọn file → đọc dấu phân cách (tự nhận hoặc chọn) + tên cột từ dòng đầu; danh sách cột có ô
/// tích; file kết quả mặc định cạnh file nguồn.
/// </summary>
public abstract partial class CsvSourceViewModel : SourceFileViewModel
{
    public static readonly string[] DelimiterNames = ["Tự nhận", "Dấu phẩy (,)", "Tab", "Dấu chấm phẩy (;)", "Gạch đứng (|)"];
    protected static readonly char[] DelimiterChars = [',', '\t', ';', '|'];

    public ObservableCollection<CsvColumnItem> Columns { get; } = [];

    [ObservableProperty]
    private int _delimiterIndex;

    [ObservableProperty]
    private bool _hasHeader = true;

    [ObservableProperty]
    private string _layoutInfo = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string _outputPath = string.Empty;

    /// <summary>Dấu phân cách đã đọc được (sau khi tự nhận).</summary>
    protected char DetectedDelimiter { get; private set; } = ',';

    protected char? ChosenDelimiter => DelimiterIndex > 0 ? DelimiterChars[DelimiterIndex - 1] : null;

    /// <summary>Hậu tố tên file kết quả mặc định ("_cot", "_khongtrung"...).</summary>
    protected abstract string OutputSuffix { get; }

    /// <summary>Cột mặc định được tích khi nạp cột.</summary>
    protected virtual bool CheckByDefault => false;

    protected virtual string OutputExtension(string sourcePath) => Path.GetExtension(sourcePath);

    protected override void OnSourceChanged(string path)
    {
        if (File.Exists(path))
        {
            OutputPath = DefaultOutput(path);
        }
        RunCommand.NotifyCanExecuteChanged();
        _ = ReloadColumnsAsync();
    }

    protected string DefaultOutput(string path) =>
        Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + OutputSuffix + OutputExtension(path));

    partial void OnDelimiterIndexChanged(int value) => _ = ReloadColumnsAsync();

    partial void OnHasHeaderChanged(bool value) => _ = ReloadColumnsAsync();

    private async Task ReloadColumnsAsync()
    {
        var path = SourcePath;
        Columns.Clear();
        LayoutInfo = string.Empty;
        if (!File.Exists(path))
        {
            return;
        }
        try
        {
            var chosen = ChosenDelimiter;
            var header = HasHeader;
            var layout = await Task.Run(() => CsvLayout.Read(path, chosen, header));
            if (path != SourcePath)
            {
                return;
            }
            DetectedDelimiter = layout.Delimiter;
            foreach (var (name, i) in layout.Columns.Select((n, i) => (n, i)))
            {
                Columns.Add(new CsvColumnItem(i, name) { IsChecked = CheckByDefault });
            }
            LayoutInfo = $"{Csv.Describe(layout.Delimiter)} · {layout.Columns.Count} cột" + (layout.Columns.Count <= 1 ? " - chỉ 1 cột: kiểm tra lại dấu phân cách" : string.Empty);
            OnColumnsLoaded();
        }
        catch (Exception ex)
        {
            LayoutInfo = "Không đọc được cột: " + ex.Message;
        }
    }

    protected virtual void OnColumnsLoaded()
    {
    }

    [RelayCommand]
    private async Task BrowseOutput()
    {
        var suggested = string.IsNullOrEmpty(OutputPath) ? "ketqua.csv" : Path.GetFileName(OutputPath);
        if (await Pickers.PickSaveFileAsync(suggested) is { } path)
        {
            OutputPath = path;
        }
    }

    protected virtual bool CanRun() => HasSource() && !string.IsNullOrWhiteSpace(OutputPath);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task Run() => RunCoreAsync();

    protected abstract Task RunCoreAsync();
}

/// <summary>Trang "Tách theo cột": mỗi giá trị của 1 cột → 1 file.</summary>
public sealed partial class CsvSplitViewModel : CsvSourceViewModel
{
    [ObservableProperty]
    private int _selectedColumnIndex = -1;

    protected override string OutputSuffix => "_theocot";

    /// <summary>Ở trang này "đầu ra" là 1 thư mục.</summary>
    protected override string OutputExtension(string sourcePath) => string.Empty;

    protected override void OnColumnsLoaded() => SelectedColumnIndex = Columns.Count > 0 ? 0 : -1;

    protected override bool CanRun() => base.CanRun() && SelectedColumnIndex >= 0;

    partial void OnSelectedColumnIndexChanged(int value)
    {
        if (File.Exists(SourcePath) && value >= 0 && value < Columns.Count)
        {
            var name = new string(Columns[value].Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            OutputPath = Path.Combine(Path.GetDirectoryName(SourcePath)!, $"{Path.GetFileNameWithoutExtension(SourcePath)}_theo_{name}");
        }
        RunCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task BrowseFolder()
    {
        if (await Pickers.PickFolderAsync() is { } folder)
        {
            OutputPath = folder;
        }
    }

    protected override Task RunCoreAsync()
    {
        var options = new CsvSplitOptions
        {
            SourcePath = SourcePath,
            OutputFolder = OutputPath,
            Column = SelectedColumnIndex,
            Delimiter = ChosenDelimiter,
            HasHeader = HasHeader,
            Encoding = Output.Encoding,
            Newline = Output.Newline,
        };
        var column = Columns[SelectedColumnIndex].Name;
        return RunAsync($"Tách theo cột \"{column}\"", (progress, ct) =>
        {
            var r = CsvColumnSplitter.Split(options, progress, ct);
            var notes = new List<string> { $"{r.Parts.Count:N0} giá trị → {r.Parts.Count:N0} file, {r.Records:N0} bản ghi → {options.OutputFolder}" };
            notes.AddRange(r.Parts.Take(20).Select(p => $"   {(p.Value.Length == 0 ? "(trống)" : p.Value)}: {p.Records:N0} bản ghi → {Path.GetFileName(p.Path)}"));
            if (r.Parts.Count > 20)
            {
                notes.Add($"   ... và {r.Parts.Count - 20:N0} giá trị nữa");
            }
            return (options.OutputFolder, notes);
        });
    }
}

/// <summary>Trang "Bỏ dòng trùng": so cả dòng hoặc theo các cột được tích; giữ lần xuất hiện đầu.</summary>
public sealed partial class DedupeViewModel : CsvSourceViewModel
{
    /// <summary>0 = cả dòng, 1 = theo cột đã tích.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ByColumns))]
    private int _modeIndex;

    [ObservableProperty]
    private bool _ignoreCase;

    [ObservableProperty]
    private bool _trim;

    public bool ByColumns => ModeIndex == 1;

    protected override string OutputSuffix => "_khongtrung";

    protected override Task RunCoreAsync()
    {
        var keys = ByColumns ? Columns.Where(c => c.IsChecked).Select(c => c.Index).ToList() : null;
        if (keys is { Count: 0 })
        {
            AddMessage("✖ Chọn ít nhất 1 cột để so, hoặc chuyển sang \"So cả dòng\".");
            return Task.CompletedTask;
        }
        var options = new DedupeOptions
        {
            SourcePath = SourcePath,
            OutputPath = OutputPath,
            KeyColumns = keys,
            IgnoreCase = IgnoreCase,
            Trim = Trim,
            HasHeader = HasHeader,
            Delimiter = ChosenDelimiter,
            Encoding = Output.Encoding,
            Newline = Output.Newline,
        };
        return RunAsync("Bỏ dòng trùng", (progress, ct) =>
        {
            var r = CsvDeduplicator.Dedupe(options, progress, ct);
            var note = $"Giữ {r.Kept:N0} · bỏ {r.Removed:N0} dòng trùng → {options.OutputPath}";
            return (options.OutputPath, r.Removed == 0
                ? [note]
                : [note, "   Dòng trùng đầu tiên (số thứ tự trong file nguồn): " + string.Join(", ", r.DuplicateSamples.Select(n => n.ToString("N0")))]);
        });
    }
}

/// <summary>Trang "Chọn / sắp cột": tích cột cần giữ, đổi thứ tự bằng Lên / Xuống, tuỳ chọn đổi luôn dấu phân cách.</summary>
public sealed partial class ColumnsViewModel : CsvSourceViewModel
{
    public static readonly string[] OutputDelimiterNames = ["Giữ như nguồn", "Dấu phẩy (,)", "Tab", "Dấu chấm phẩy (;)", "Gạch đứng (|)"];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand), nameof(MoveDownCommand))]
    private CsvColumnItem? _selectedColumn;

    [ObservableProperty]
    private int _outputDelimiterIndex;

    [ObservableProperty]
    private bool _quoteAll;

    protected override string OutputSuffix => "_cot";

    protected override bool CheckByDefault => true;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveDown() => Move(1);

    private bool HasSelection() => SelectedColumn is not null;

    private void Move(int delta)
    {
        if (SelectedColumn is not { } item)
        {
            return;
        }
        int from = Columns.IndexOf(item), to = from + delta;
        if (to >= 0 && to < Columns.Count)
        {
            Columns.Move(from, to);
            SelectedColumn = item;
        }
    }

    [RelayCommand]
    private void CheckAll()
    {
        foreach (var c in Columns)
        {
            c.IsChecked = true;
        }
    }

    [RelayCommand]
    private void CheckNone()
    {
        foreach (var c in Columns)
        {
            c.IsChecked = false;
        }
    }

    [RelayCommand]
    private void ResetOrder()
    {
        var ordered = Columns.OrderBy(c => c.Index).ToList();
        Columns.Clear();
        foreach (var c in ordered)
        {
            Columns.Add(c);
        }
    }

    protected override Task RunCoreAsync()
    {
        var picked = Columns.Where(c => c.IsChecked).Select(c => c.Index).ToList();
        if (picked.Count == 0)
        {
            AddMessage("✖ Chưa tích cột nào.");
            return Task.CompletedTask;
        }
        var options = new CsvTransformOptions
        {
            SourcePath = SourcePath,
            OutputPath = OutputPath,
            InputDelimiter = ChosenDelimiter,
            OutputDelimiter = OutputDelimiterIndex > 0 ? DelimiterChars[OutputDelimiterIndex - 1] : null,
            Columns = picked,
            QuoteAll = QuoteAll,
            Encoding = Output.Encoding,
            Newline = Output.Newline,
        };
        return RunAsync($"Ghi {picked.Count} cột", (progress, ct) =>
        {
            var r = CsvTransformer.Transform(options, progress, ct);
            var notes = new List<string> { $"{r.Records:N0} bản ghi, {picked.Count} cột → {options.OutputPath}" };
            if (r.ShortRecords > 0)
            {
                notes.Add($"   ⚠ {r.ShortRecords:N0} bản ghi thiếu cột đã chọn - ô thiếu để trống");
            }
            return (options.OutputPath, notes);
        });
    }
}

/// <summary>Trang "Đổi dấu phân cách": , ↔ Tab ↔ ; ↔ |, giữ nguyên mọi cột.</summary>
public sealed partial class DelimiterViewModel : CsvSourceViewModel
{
    public static readonly string[] TargetNames = ["Dấu phẩy (,)", "Tab", "Dấu chấm phẩy (;)", "Gạch đứng (|)"];

    [ObservableProperty]
    private int _targetIndex = 1;

    [ObservableProperty]
    private bool _quoteAll;

    protected override string OutputSuffix => "_doi";

    protected override string OutputExtension(string sourcePath) => TargetIndex == 1 ? ".tsv" : ".csv";

    partial void OnTargetIndexChanged(int value)
    {
        if (File.Exists(SourcePath))
        {
            OutputPath = DefaultOutput(SourcePath);
        }
    }

    protected override Task RunCoreAsync()
    {
        var options = new CsvTransformOptions
        {
            SourcePath = SourcePath,
            OutputPath = OutputPath,
            InputDelimiter = ChosenDelimiter,
            OutputDelimiter = DelimiterChars[Math.Clamp(TargetIndex, 0, 3)],
            QuoteAll = QuoteAll,
            Encoding = Output.Encoding,
            Newline = Output.Newline,
        };
        return RunAsync("Đổi dấu phân cách", (progress, ct) =>
        {
            var r = CsvTransformer.Transform(options, progress, ct);
            return (options.OutputPath, [$"{Csv.Describe(r.InputDelimiter)} → {Csv.Describe(r.OutputDelimiter)}: {r.Records:N0} bản ghi → {options.OutputPath}"]);
        });
    }
}
