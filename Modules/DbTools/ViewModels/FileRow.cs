using CommunityToolkit.Mvvm.ComponentModel;
using DbTools.Core;

namespace DbTools.ViewModels;

public enum RowStatus
{
    None,
    Ok,
    Info,
    Warning,
    Error,
    Running,
    NotRun,
}

/// <summary>1 dòng trong danh sách file: thứ tự, loại lệnh, cảnh báo, kết quả kiểm tra / chạy.</summary>
public sealed partial class FileRow : ObservableObject
{
    public FileRow(SqlScriptFile file)
    {
        _file = file;
        ApplyIssues(file.Issues, string.Empty);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RelativePath), nameof(FullPath), nameof(Kind), nameof(Danger), nameof(HasDanger), nameof(StatementCount))]
    private SqlScriptFile _file;

    [ObservableProperty]
    private int _number;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusGlyph))]
    private RowStatus _status;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>Chi tiết (tooltip / ô dưới danh sách): các điều cần báo, lỗi kèm số dòng.</summary>
    [ObservableProperty]
    private string _detail = string.Empty;

    public string RelativePath => File.RelativePath;
    public string FullPath => File.FullPath;
    public string Kind => File.KindSummary;
    public string Danger => File.DangerSummary;
    public bool HasDanger => Danger.Length > 0;
    public int StatementCount => File.Statements.Count;

    public string StatusGlyph => Status switch
    {
        RowStatus.Ok => "",       // ✓
        RowStatus.Info => "",     // i
        RowStatus.Warning => "",  // !
        RowStatus.Error => "",    // ✗
        RowStatus.Running => "",  // ▶
        RowStatus.NotRun => "",   // –
        _ => string.Empty,
    };

    /// <summary>Đặt trạng thái theo danh sách điều cần báo (mức cao nhất thắng). <paramref name="okText"/> khi không có gì.</summary>
    public void ApplyIssues(IReadOnlyList<ScriptIssue> issues, string okText)
    {
        var errors = issues.Count(i => i.Level == IssueLevel.Error);
        var warnings = issues.Count(i => i.Level == IssueLevel.Warning);
        var infos = issues.Count(i => i.Level == IssueLevel.Info);
        (Status, StatusText) = (errors, warnings, infos) switch
        {
            ( > 0, _, _) => (RowStatus.Error, $"{errors} lỗi" + (warnings > 0 ? $", {warnings} cảnh báo" : string.Empty)),
            (_, > 0, _) => (RowStatus.Warning, $"{warnings} cảnh báo"),
            (_, _, > 0) when okText.Length > 0 => (RowStatus.Info, okText),
            _ when okText.Length > 0 => (RowStatus.Ok, okText),
            _ => (RowStatus.None, string.Empty),
        };
        Detail = string.Join(Environment.NewLine, issues.OrderBy(i => i.Line).Select(i => $"[{LevelText(i.Level)}] {i}"));
    }

    public void SetResult(RowStatus status, string text, string detail = "")
    {
        Status = status;
        StatusText = text;
        if (detail.Length > 0)
        {
            Detail = detail;
        }
    }

    public static string LevelText(IssueLevel level) => level switch
    {
        IssueLevel.Error => "Lỗi",
        IssueLevel.Warning => "Cảnh báo",
        _ => "Thông tin",
    };
}
