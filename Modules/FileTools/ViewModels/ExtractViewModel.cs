using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTools.Core;
using FileTools.Services;

namespace FileTools.ViewModels;

/// <summary>Trang "Trích dòng": N dòng đầu / dòng X–Y / N dòng cuối → file tạm (%TEMP%\AppSuite\FileTools) rồi mở.</summary>
public sealed partial class ExtractViewModel : SourceFileViewModel
{
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
        ExtractCommand.NotifyCanExecuteChanged();
    }

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
