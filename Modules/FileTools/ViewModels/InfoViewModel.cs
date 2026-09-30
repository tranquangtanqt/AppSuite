using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using FileTools.Core;

namespace FileTools.ViewModels;

public sealed record InfoRow(string Label, string Value);

/// <summary>Trang "Thông tin file": 1 lượt đọc → số dòng, kiểu xuống dòng, encoding, dòng dài nhất; file dạng bảng thêm
/// dấu phân cách, số cột, bản ghi lệch cột. Chọn file là tự phân tích.</summary>
public sealed partial class InfoViewModel : SourceFileViewModel
{
    public ObservableCollection<InfoRow> Rows { get; } = [];

    protected override void OnSourceChanged(string path)
    {
        InspectCommand.NotifyCanExecuteChanged();
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
}
