using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTools.Core;
using FileTools.Services;

namespace FileTools.ViewModels;

/// <summary>Trang "Tách file": theo dung lượng / số dòng / số phần, CSV lặp dòng tiêu đề.</summary>
public sealed partial class SplitViewModel : SourceFileViewModel
{
    /// <summary>0 = dung lượng, 1 = số dòng, 2 = số phần (thứ tự RadioButtons trong XAML = <see cref="SplitMode"/>).</summary>
    [ObservableProperty]
    private int _modeIndex;

    [ObservableProperty]
    private double _sizeMb = 100;

    [ObservableProperty]
    private double _linesPerPart = 1_000_000;

    [ObservableProperty]
    private double _parts = 4;

    [ObservableProperty]
    private bool _repeatHeader;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SplitCommand))]
    private string _outputFolder = string.Empty;

    protected override void OnSourceChanged(string path)
    {
        RepeatHeader = Csv.IsCsvPath(path);
        if (File.Exists(path))
        {
            OutputFolder = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "_parts");
        }
        SplitCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task BrowseOutput()
    {
        if (await Pickers.PickFolderAsync() is { } folder)
        {
            OutputFolder = folder;
        }
    }

    private bool CanSplit() => HasSource() && !string.IsNullOrWhiteSpace(OutputFolder);

    [RelayCommand(CanExecute = nameof(CanSplit))]
    private Task Split()
    {
        var options = new SplitOptions
        {
            SourcePath = SourcePath,
            OutputFolder = OutputFolder,
            Mode = (SplitMode)ModeIndex,
            SizeBytes = (long)(Math.Max(0.001, SizeMb) * 1024 * 1024),
            Lines = (long)Math.Max(1, LinesPerPart),
            Parts = (int)Math.Clamp(Parts, 1, 10_000),
            RepeatHeader = RepeatHeader,
            Encoding = Output.Encoding,
            Newline = Output.Newline,
        };
        return RunAsync($"Tách {Path.GetFileName(SourcePath)}", (progress, ct) =>
        {
            var r = FileSplitter.Split(options, progress, ct);
            var notes = new List<string>();
            if (r.Warning is { } warning)
            {
                notes.Add(warning);
            }
            notes.Add($"{r.Parts.Count} phần, {r.Lines:N0} dòng dữ liệu → {options.OutputFolder}");
            notes.AddRange(r.Parts.Take(20).Select(p => $"   {Path.GetFileName(p.Path)}: {p.Lines:N0} dòng, {FileItem.FormatSize(p.Bytes)}"));
            if (r.Parts.Count > 20)
            {
                notes.Add($"   ... và {r.Parts.Count - 20} phần nữa");
            }
            return (options.OutputFolder, notes);
        });
    }
}
