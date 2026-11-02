using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTools.Core;
using FileTools.Services;

namespace FileTools.ViewModels;

/// <summary>Kiểu so khớp dùng chung cho Tìm / Lọc dòng.</summary>
public abstract partial class MatchViewModel : SourceFileViewModel
{
    [ObservableProperty]
    private bool _isRegex;

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private bool _ignoreDiacritics = true;

    protected MatchOptions BuildMatch(IEnumerable<string> terms, bool all = false) => new()
    {
        Terms = terms.Where(t => !string.IsNullOrEmpty(t)).ToList(),
        IsRegex = IsRegex,
        MatchCase = MatchCase,
        IgnoreDiacritics = IgnoreDiacritics,
        MatchAll = all,
    };
}

/// <summary>
/// Trang "Tìm / Lọc dòng" (gộp 2 trang cũ - cùng 1 kiểu so khớp): ô từ khoá chung (mỗi dòng 1 từ; regex: dòng đầu).
/// <b>Tìm</b>: hiện tối đa N dòng khớp (vẫn đếm hết), tuỳ chọn xuất mọi dòng khớp ra file tạm. <b>Lọc ra file</b>: giữ / bỏ
/// dòng khớp, ghi ra file mới (CSV lọc theo bản ghi).
/// </summary>
public sealed partial class SearchViewModel : MatchViewModel
{
    public ObservableCollection<SearchHit> Hits { get; } = [];

    /// <summary>Từ khoá, mỗi dòng 1 từ (trang Tìm cũ lưu ô này trong mẫu với tên "Query" - xem PresetMigration).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand), nameof(FilterCommand))]
    private string _terms = string.Empty;

    /// <summary>0 = chứa 1 trong các từ, 1 = chứa tất cả.</summary>
    [ObservableProperty]
    private int _matchAllIndex;

    [ObservableProperty]
    private double _maxResults = 10_000;

    [ObservableProperty]
    private bool _exportMatches;

    [ObservableProperty]
    private bool _exportLineNumbers = true;

    [ObservableProperty]
    private string _summary = string.Empty;

    /// <summary>Bấm đúp 1 dòng kết quả: trích N dòng trước + N dòng sau nó ra file tạm rồi mở.</summary>
    [ObservableProperty]
    private double _contextLines = 20;

    /// <summary>File của lần Tìm gần nhất - kết quả đang hiện thuộc file này dù ô File nguồn đã đổi.</summary>
    private string _searchedPath = string.Empty;

    /// <summary>0 = giữ dòng khớp, 1 = bỏ dòng khớp.</summary>
    [ObservableProperty]
    private int _actionIndex;

    [ObservableProperty]
    private bool _keepHeader;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FilterCommand))]
    private string _outputPath = string.Empty;

    protected override void OnSourceChanged(string path)
    {
        KeepHeader = Csv.IsCsvPath(path);
        if (File.Exists(path))
        {
            OutputPath = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "_loc" + Path.GetExtension(path));
        }
        SearchCommand.NotifyCanExecuteChanged();
        FilterCommand.NotifyCanExecuteChanged();
    }

    private string[] TermList => Terms.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    private bool CanSearch() => HasSource() && Terms.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task Search()
    {
        var terms = TermList;
        var options = new SearchOptions
        {
            SourcePath = SourcePath,
            Match = BuildMatch(terms, MatchAllIndex == 1),
            MaxResults = (int)Math.Clamp(MaxResults, 1, 1_000_000),
            ExportPath = ExportMatches
                ? Path.Combine(Shell.TempFolder, $"{Path.GetFileNameWithoutExtension(SourcePath)}_tim_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(SourcePath)}")
                : null,
            ExportLineNumbers = ExportLineNumbers,
            Encoding = Output.Encoding,
            Newline = Output.Newline,
        };
        Hits.Clear();
        Summary = string.Empty;
        SearchResult? result = null;
        string title = terms.Length == 1 ? $"Tìm \"{terms[0]}\"" : $"Tìm {terms.Length} từ khoá";
        await RunAsync(title, (progress, ct) =>
        {
            result = TextSearcher.Search(options, progress, ct);
            var note = $"{result.TotalMatches:N0} dòng khớp / {result.LinesScanned:N0} dòng";
            return (options.ExportPath, options.ExportPath is null ? [note] : [note, "Đã xuất dòng khớp → " + options.ExportPath]);
        });
        if (result is null)
        {
            return;
        }
        _searchedPath = options.SourcePath;
        foreach (var hit in result.Hits)
        {
            Hits.Add(hit);
        }
        Summary = result.TotalMatches == 0
            ? "Không tìm thấy."
            : $"{result.TotalMatches:N0} dòng khớp" + (result.Truncated ? $" - hiện {result.Hits.Count:N0} dòng đầu" : string.Empty);
    }

    /// <summary>Trích ±N dòng quanh 1 dòng khớp ra file tạm (tên có số dòng + ngày giờ) rồi mở. File CSV kèm dòng tiêu đề.</summary>
    [RelayCommand]
    private async Task ExtractAround(SearchHit? hit)
    {
        if (hit is null || !File.Exists(_searchedPath))
        {
            return;
        }
        long n = (long)Math.Clamp(ContextLines, 0, 1_000_000);
        long from = Math.Max(1, hit.LineNumber - n);
        bool header = Csv.IsCsvPath(_searchedPath) && from > 1;
        var options = new ExtractOptions
        {
            SourcePath = _searchedPath,
            OutputPath = Path.Combine(Shell.TempFolder, $"{Path.GetFileNameWithoutExtension(_searchedPath)}_quanh{hit.LineNumber}_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(_searchedPath)}"),
            Mode = ExtractMode.Range,
            From = from,
            To = hit.LineNumber + n,
            IncludeHeader = header,
            Encoding = Output.Encoding,
            Newline = Output.Newline,
        };
        await RunAsync($"Trích quanh dòng {hit.LineNumber:N0}", (progress, ct) =>
        {
            var r = LineExtractor.Extract(options, progress, ct);
            long at = hit.LineNumber - from + 1 + (header ? 1 : 0);
            return (options.OutputPath,
            [
                $"{r.Lines:N0} dòng (dòng {from:N0}–{from + r.Lines - 1 - (header ? 1 : 0):N0}{(header ? " + tiêu đề" : string.Empty)}) → {options.OutputPath}",
                $"Dòng {hit.LineNumber:N0} là dòng {at:N0} trong file trích",
            ]);
        });
        if (ResultPath == options.OutputPath && File.Exists(options.OutputPath))
        {
            OpenResultCommand.Execute(null);
        }
    }

    [RelayCommand]
    private async Task BrowseOutput()
    {
        if (await Pickers.PickSaveFileAsync(string.IsNullOrEmpty(OutputPath) ? "loc.txt" : Path.GetFileName(OutputPath)) is { } path)
        {
            OutputPath = path;
        }
    }

    private bool CanFilter() => HasSource() && Terms.Trim().Length > 0 && !string.IsNullOrWhiteSpace(OutputPath);

    [RelayCommand(CanExecute = nameof(CanFilter))]
    private Task Filter()
    {
        var options = new FilterOptions
        {
            SourcePath = SourcePath,
            OutputPath = OutputPath,
            Match = BuildMatch(TermList, MatchAllIndex == 1),
            KeepMatching = ActionIndex == 0,
            KeepHeader = KeepHeader,
            Encoding = Output.Encoding,
            Newline = Output.Newline,
        };
        return RunAsync("Lọc dòng", (progress, ct) =>
        {
            var r = LineFilter.Filter(options, progress, ct);
            return (options.OutputPath, [$"Giữ {r.Kept:N0} · bỏ {r.Removed:N0} → {options.OutputPath}"]);
        });
    }
}
