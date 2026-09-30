using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTools.Core;
using FileTools.Services;

namespace FileTools.ViewModels;

/// <summary>1 dòng trong danh sách file nguồn.</summary>
public sealed class FileItem(FileInfo info)
{
    public string FullName { get; } = info.FullName;
    public string Name { get; } = info.Name;
    public string Folder { get; } = info.DirectoryName ?? string.Empty;
    public long Length { get; } = info.Length;
    public string SizeText { get; } = FormatSize(info.Length);
    public string Modified { get; } = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm");

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.##} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.##} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B",
    };
}

/// <summary>Trang "Nối file": liệt kê file trong thư mục (mẫu lọc, thư mục con, thứ tự) → nối thành 1 file.</summary>
public sealed partial class MergeViewModel : JobViewModel
{
    public static readonly string[] SortNames = ["Tên (hiểu số: 2 trước 10)", "Ngày sửa", "Dung lượng"];

    public ObservableCollection<FileItem> Files { get; } = [];

    [ObservableProperty]
    private string _sourceFolder = string.Empty;

    [ObservableProperty]
    private string _patterns = "*.txt;*.csv;*.tsv;*.log";

    [ObservableProperty]
    private bool _recursive;

    [ObservableProperty]
    private int _sortIndex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand), nameof(MoveDownCommand), nameof(RemoveCommand))]
    private FileItem? _selectedFile;

    [ObservableProperty]
    private bool _fileNameHeader;

    [ObservableProperty]
    private bool _blankLineBetween;

    [ObservableProperty]
    private bool _csvHeaderOnce;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MergeCommand))]
    private string _outputPath = string.Empty;

    [ObservableProperty]
    private string _summary = "Chưa có file nào.";

    public MergeViewModel()
    {
        Files.CollectionChanged += (_, _) =>
        {
            Summary = Files.Count == 0 ? "Chưa có file nào." : $"{Files.Count} file · {FileItem.FormatSize(Files.Sum(f => f.Length))}";
            MergeCommand.NotifyCanExecuteChanged();
        };
    }

    [RelayCommand]
    private async Task BrowseFolder()
    {
        if (await Pickers.PickFolderAsync() is { } folder)
        {
            SourceFolder = folder;
            ListFiles();
        }
    }

    [RelayCommand]
    private void ListFiles()
    {
        Files.Clear();
        if (!Directory.Exists(SourceFolder))
        {
            AddMessage("✖ Không tìm thấy thư mục: " + SourceFolder);
            return;
        }
        try
        {
            foreach (var file in FileListing.List(SourceFolder, Patterns, Recursive, (FileSortOrder)SortIndex))
            {
                Files.Add(new FileItem(file));
            }
        }
        catch (Exception ex)
        {
            AddMessage("✖ Không liệt kê được: " + ex.Message);
            return;
        }
        CsvHeaderOnce = Files.Count > 0 && Files.All(f => Csv.IsCsvPath(f.FullName));
        if (string.IsNullOrEmpty(OutputPath) && Files.Count > 0)
        {
            OutputPath = Path.Combine(SourceFolder, $"merged_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(Files[0].Name)}");
        }
    }

    /// <summary>Kéo-thả file (hoặc thư mục) vào danh sách.</summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                SourceFolder = path;
                ListFiles();
            }
            else if (File.Exists(path) && Files.All(f => !string.Equals(f.FullName, path, StringComparison.OrdinalIgnoreCase)))
            {
                Files.Add(new FileItem(new FileInfo(path)));
            }
        }
    }

    [RelayCommand]
    private void Resort()
    {
        var sorted = FileListing.Sort(Files.Select(f => new FileInfo(f.FullName)), (FileSortOrder)SortIndex).ToList();
        Files.Clear();
        foreach (var file in sorted)
        {
            Files.Add(new FileItem(file));
        }
    }

    [RelayCommand(CanExecute = nameof(CanMove))]
    private void MoveUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(CanMove))]
    private void MoveDown() => Move(1);

    [RelayCommand(CanExecute = nameof(CanMove))]
    private void Remove()
    {
        if (SelectedFile is { } item)
        {
            int index = Files.IndexOf(item);
            Files.Remove(item);
            SelectedFile = Files.Count > 0 ? Files[Math.Min(index, Files.Count - 1)] : null;
        }
    }

    private bool CanMove() => SelectedFile is not null;

    private void Move(int delta)
    {
        if (SelectedFile is not { } item)
        {
            return;
        }
        int from = Files.IndexOf(item), to = from + delta;
        if (to >= 0 && to < Files.Count)
        {
            Files.Move(from, to);
            SelectedFile = item;
        }
    }

    [RelayCommand]
    private async Task BrowseOutput()
    {
        var suggested = string.IsNullOrEmpty(OutputPath) ? "merged.txt" : Path.GetFileName(OutputPath);
        if (await Pickers.PickSaveFileAsync(suggested) is { } path)
        {
            OutputPath = path;
        }
    }

    private bool CanMerge() => Files.Count > 0 && !string.IsNullOrWhiteSpace(OutputPath);

    [RelayCommand(CanExecute = nameof(CanMerge))]
    private Task Merge()
    {
        var options = new MergeOptions
        {
            Files = Files.Select(f => f.FullName).ToList(),
            OutputPath = OutputPath,
            Encoding = Output.Encoding,
            Newline = Output.Newline,
            FileNameHeader = FileNameHeader,
            BlankLineBetween = BlankLineBetween,
            CsvHeaderOnce = CsvHeaderOnce,
        };
        return RunAsync($"Nối {options.Files.Count} file", (progress, ct) =>
        {
            var r = FileMerger.Merge(options, progress, ct);
            return (options.OutputPath, r.Notes.Append($"{r.Files} file → {r.Lines:N0} dòng, {FileItem.FormatSize(r.BytesWritten)}: {options.OutputPath}"));
        });
    }
}
