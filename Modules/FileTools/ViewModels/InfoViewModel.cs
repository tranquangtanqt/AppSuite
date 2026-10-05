using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTools.Core;
using FileTools.Services;

namespace FileTools.ViewModels;

public sealed record InfoRow(string Label, string Value);

/// <summary>Trang "Xem file" (gộp 2 trang cũ Thông tin file + Trích dòng). Thông tin: 1 lượt đọc → số dòng, kiểu xuống dòng,
/// encoding, dòng dài nhất; file dạng bảng thêm dấu phân cách, số cột, bản ghi lệch cột - chọn file là tự phân tích.
/// Trích dòng: N dòng đầu / dòng X–Y / N dòng cuối → file tạm (%TEMP%\AppSuite\FileTools) rồi mở.</summary>
public sealed partial class InfoViewModel : SourceFileViewModel
{
    public ObservableCollection<InfoRow> Rows { get; } = [];

    /// <summary>0 = đầu, 1 = khoảng, 2 = cuối (= <see cref="ExtractMode"/>).</summary>
    [ObservableProperty]
    private int _modeIndex;

    [ObservableProperty]
    private double _count = 1000;

    [ObservableProperty]
    private double _from = 1;

    [ObservableProperty]
    private double _to = 1000;

    [ObservableProperty]
    private bool _includeHeader;

    [ObservableProperty]
    private bool _openAfter = true;

    public string TempFolder => Shell.TempFolder;

    protected override void OnSourceChanged(string path)
    {
        IncludeHeader = Csv.IsCsvPath(path);
        InspectCommand.NotifyCanExecuteChanged();
        ExtractCommand.NotifyCanExecuteChanged();
        if (File.Exists(path) && !IsBusy)
        {
            InspectCommand.Execute(null);
        }
    }
    [RelayCommand(CanExecute = nameof(HasSource))]
    private async Task Inspect()
    {
        var path = SourcePath;
        FileReport? report = null;
        await RunAsync($"Phân tích {Path.GetFileName(path)}", (progress, ct) =>
        {
            report = FileInspector.Inspect(path, progress, ct);
            return (null, [$"{report.Lines:N0} dòng · {FileItem.FormatSize(report.Size)}"]);
        });
        if (report is null)
        {
            return;
        }
        Rows.Clear();
        Add("File", report.Path);
        Add("Dung lượng", $"{FileItem.FormatSize(report.Size)} ({report.Size:N0} byte)");
        Add("Encoding", report.Encoding.Name + (report.Encoding.Warning is { } w ? $" - {w}" : string.Empty));
        Add("Số dòng", $"{report.Lines:N0} (dòng trống: {report.EmptyLines:N0})");
        Add("Xuống dòng", report.NewlineStyle);
        Add("Dòng cuối có xuống dòng", report.EndsWithNewline ? "Có" : "Không");
        Add("Dòng dài nhất", report.Lines == 0 ? "-" : $"{report.LongestLineLength:N0} ký tự (dòng {report.LongestLineNumber:N0})");
        if (report.Delimiter is { } d)
        {
            Add("Dấu phân cách", Csv.Describe(d));
            Add("Số cột (theo dòng tiêu đề)", report.HeaderColumns.ToString());
            Add("Tên cột", string.Join(" | ", report.HeaderNames));
            Add("Số bản ghi (kể cả tiêu đề)", report.Records.ToString("N0") + (report.Records != report.Lines ? $" - khác số dòng vì có ô xuống dòng trong ngoặc kép" : string.Empty));
            Add("Bản ghi lệch số cột", report.MismatchedRecords == 0
                ? "Không có"
                : $"{report.MismatchedRecords:N0} - bản ghi số {string.Join(", ", report.MismatchSamples.Select(n => n.ToString("N0")))}{(report.MismatchedRecords > report.MismatchSamples.Count ? ", ..." : string.Empty)}");
        }
    }

    private void Add(string label, string value) => Rows.Add(new InfoRow(label, value));

    [RelayCommand]
    private void OpenTempFolder()
    {
        Directory.CreateDirectory(Shell.TempFolder);
        Shell.Open(Shell.TempFolder);
    }

    [RelayCommand(CanExecute = nameof(HasSource))]
    private async Task Extract()
    {
        var mode = (ExtractMode)ModeIndex;
        string tag = mode switch
        {
            ExtractMode.Head => $"dau{(long)Count}",
            ExtractMode.Tail => $"cuoi{(long)Count}",
            _ => $"dong{(long)From}-{(long)To}",
        };
        var options = new ExtractOptions
        {
            SourcePath = SourcePath,
            OutputPath = Path.Combine(Shell.TempFolder, $"{Path.GetFileNameWithoutExtension(SourcePath)}_{tag}_{DateTime.Now:HHmmss}{Path.GetExtension(SourcePath)}"),
            Mode = mode,
            Count = (long)Math.Max(1, Count),
            From = (long)Math.Max(1, From),
            To = (long)Math.Max(1, To),
            IncludeHeader = IncludeHeader,
            Encoding = Output.Encoding,
            Newline = Output.Newline,
        };
        await RunAsync("Trích dòng", (progress, ct) =>
        {
            var r = LineExtractor.Extract(options, progress, ct);
            string where = r.FirstLineNumber is { } first ? $" (từ dòng {first:N0})" : string.Empty;
            return (options.OutputPath, [$"{r.Lines:N0} dòng{where}, {r.Encoding} → {options.OutputPath}"]);
        });
        if (OpenAfter && ResultPath == options.OutputPath && File.Exists(options.OutputPath))
        {
            OpenResultCommand.Execute(null);
        }
    }
}
