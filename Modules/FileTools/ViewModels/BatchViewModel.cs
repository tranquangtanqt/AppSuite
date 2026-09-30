using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTools.Core;
using FileTools.Services;

namespace FileTools.ViewModels;

/// <summary>1 file trong danh sách xử lý hàng loạt; <see cref="Detail"/> (encoding / kiểu xuống dòng hiện tại) điền sau.</summary>
public sealed partial class BatchFileItem(FileInfo info) : ObservableObject
{
    public string FullName { get; } = info.FullName;
    public string Name { get; } = info.Name;
    public string SizeText { get; } = FileItem.FormatSize(info.Length);

    [ObservableProperty]
    private string _detail = "...";
}

/// <summary>
/// Nền cho trang xử lý hàng loạt (Đổi encoding / Đổi xuống dòng): danh sách file (thêm file, thêm cả thư mục theo mẫu
/// lọc, kéo-thả), đầu ra = thư mục khác hoặc ghi đè file gốc (giữ .bak), rồi chạy <see cref="FileRewriter"/>.
/// </summary>
public abstract partial class BatchViewModel : JobViewModel
{
    public ObservableCollection<BatchFileItem> Files { get; } = [];

    [ObservableProperty]
    private string _patterns = "*.txt;*.csv;*.tsv;*.log";

    [ObservableProperty]
    private bool _recursive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private BatchFileItem? _selectedFile;

    /// <summary>0 = ghi ra thư mục khác, 1 = ghi đè file gốc (giữ .bak).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesOutputFolder))]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    private int _outputModeIndex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    private string _outputFolder = string.Empty;

    [ObservableProperty]
    private string _summary = "Chưa có file nào.";

    public bool UsesOutputFolder => OutputModeIndex == 0;

    protected BatchViewModel()
    {
        Files.CollectionChanged += (_, _) =>
        {
            Summary = Files.Count == 0 ? "Chưa có file nào." : $"{Files.Count} file";
            ConvertCommand.NotifyCanExecuteChanged();
        };
    }

    /// <summary>Mô tả hiện trạng 1 file (chạy ở nền) - hiện ở cột bên phải danh sách.</summary>
    protected abstract string Describe(string path);

    /// <summary>Tuỳ chọn đổi riêng của trang (encoding đích / kiểu xuống dòng đích).</summary>
    protected abstract RewriteOptions BuildOptions(IReadOnlyList<string> files, string? outputFolder);

    protected abstract string Title { get; }

    /// <summary>Đường dẫn file / thư mục dán vào (vd copy từ thanh địa chỉ Explorer); nhiều đường dẫn cách nhau bằng ";".</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTypedPathCommand))]
    private string _typedPath = string.Empty;

    [RelayCommand(CanExecute = nameof(HasTypedPath))]
    private void AddTypedPath()
    {
        var paths = TypedPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Trim('"')).ToList();
        var missing = paths.Where(p => !File.Exists(p) && !Directory.Exists(p)).ToList();
        foreach (var path in missing)
        {
            AddMessage("✖ Không tìm thấy: " + path);
        }
        AddPaths(paths.Except(missing));
        TypedPath = string.Empty;
    }

    private bool HasTypedPath() => TypedPath.Trim().Length > 0;

    [RelayCommand]
    private async Task AddFiles() => AddPaths(await Pickers.PickFilesAsync());

    [RelayCommand]
    private async Task AddFolder()
    {
        if (await Pickers.PickFolderAsync() is { } folder)
        {
            AddPaths([folder]);
        }
    }

    public void AddPaths(IEnumerable<string> paths)
    {
        var added = new List<BatchFileItem>();
        foreach (var path in paths)
        {
            IEnumerable<FileInfo> infos;
            try
            {
                infos = Directory.Exists(path)
                    ? FileListing.List(path, Patterns, Recursive, FileSortOrder.NameNatural)
                    : File.Exists(path) ? [new FileInfo(path)] : [];
            }
            catch (Exception ex)
            {
                AddMessage("✖ " + ex.Message);
                continue;
            }
            foreach (var info in infos)
            {
                if (Files.All(f => !string.Equals(f.FullName, info.FullName, StringComparison.OrdinalIgnoreCase)))
                {
                    var item = new BatchFileItem(info);
                    Files.Add(item);
                    added.Add(item);
                }
            }
            if (string.IsNullOrEmpty(OutputFolder))
            {
                var baseFolder = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
                OutputFolder = Path.Combine(baseFolder, "converted");
            }
        }
        _ = DescribeAsync(added);
    }

    private async Task DescribeAsync(IReadOnlyList<BatchFileItem> items)
    {
        foreach (var item in items)
        {
            string detail;
            try
            {
                detail = await Task.Run(() => Describe(item.FullName));
            }
            catch (Exception ex)
            {
                detail = "Lỗi: " + ex.Message;
            }
            item.Detail = detail;
        }
    }

    /// <summary>Làm mới cột hiện trạng (sau khi đổi xong).</summary>
    protected Task RefreshDetailsAsync() => DescribeAsync(Files.ToList());

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove()
    {
        if (SelectedFile is { } item)
        {
            Files.Remove(item);
        }
    }

    private bool CanRemove() => SelectedFile is not null;

    [RelayCommand]
    private void Clear() => Files.Clear();

    [RelayCommand]
    private async Task BrowseOutput()
    {
        if (await Pickers.PickFolderAsync() is { } folder)
        {
            OutputFolder = folder;
        }
    }

    private bool CanConvert() => Files.Count > 0 && (OutputModeIndex == 1 || !string.IsNullOrWhiteSpace(OutputFolder));

    [RelayCommand(CanExecute = nameof(CanConvert))]
    private async Task Convert()
    {
        string? folder = OutputModeIndex == 0 ? OutputFolder : null;
        RewriteOptions options;
        try
        {
            options = BuildOptions(Files.Select(f => f.FullName).ToList(), folder);
        }
        catch (ArgumentException ex)
        {
            // Tuỳ chọn không hợp lệ (regex sai, chưa nhập chữ cần tìm...).
            AddMessage("✖ " + ex.Message);
            return;
        }
        await RunAsync($"{Title} {options.Files.Count} file", (progress, ct) =>
        {
            var items = FileRewriter.Rewrite(options, progress, ct);
            var notes = items.Select(Format).ToList();
            int written = items.Count(i => i.Status == RewriteStatus.Written);
            int same = items.Count(i => i.Status == RewriteStatus.Unchanged);
            int failed = items.Count(i => i.Status == RewriteStatus.Failed);
            notes.Add($"Đã ghi {written} · không cần đổi {same} · lỗi {failed}" + (folder is null && written > 0 ? " · bản gốc giữ thành .bak" : string.Empty));
            return (folder ?? (items.FirstOrDefault(i => i.Output is not null)?.Output), notes);
        });
        await RefreshDetailsAsync();
    }

    private static string Format(RewriteItem i) => i.Status switch
    {
        RewriteStatus.Failed => $"   ✖ {Path.GetFileName(i.Source)}: {i.Error}",
        RewriteStatus.Unchanged => $"   = {Path.GetFileName(i.Source)}: không có gì để đổi ({i.From})",
        _ => $"   ✔ {Path.GetFileName(i.Source)}: "
            + (i.Replacements > 0 ? $"thay {i.Replacements:N0} chỗ" : $"{i.From} → {i.To}")
            + (i.EndingsChanged > 0 ? $", đổi {i.EndingsChanged:N0} xuống dòng" : string.Empty)
            + (i.LostChars > 0 ? $" ⚠ {i.LostChars:N0} ký tự không có trong {i.To}, đã thay bằng \"?\"" : string.Empty),
    };

    /// <summary>Kiểu xuống dòng của 64 KB đầu file (nhanh, đủ để xem trước).</summary>
    protected static string SampleNewlines(string path)
    {
        var sniff = EncodingSniffer.Detect(path);
        var buffer = new char[64 * 1024];
        int read;
        using (var reader = new StreamReader(LineReader.OpenRead(path), sniff.Encoding))
        {
            read = reader.ReadBlock(buffer, 0, buffer.Length);
        }
        long crlf = 0, lf = 0, cr = 0;
        for (int i = 0; i < read; i++)
        {
            if (buffer[i] == '\r')
            {
                if (i + 1 < read && buffer[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (buffer[i] == '\n')
            {
                lf++;
            }
        }
        return new FileReport { Path = path, Size = 0, Encoding = sniff, CrLf = crlf, Lf = lf, Cr = cr }.NewlineStyle;
    }
}

/// <summary>Trang "Đổi encoding" (hàng loạt): encoding đích lấy từ khối Đầu ra; đọc nguồn tự nhận hoặc ép.</summary>
public sealed partial class EncodingViewModel : BatchViewModel
{
    public static readonly string[] SourceEncodingNames = ["Tự nhận", "UTF-8", "Shift-JIS", "UTF-16 LE", "Windows-1258 (Việt)"];

    [ObservableProperty]
    private int _sourceEncodingIndex;

    protected override string Title => "Đổi encoding";

    protected override string Describe(string path)
    {
        var sniff = EncodingSniffer.Detect(path);
        return sniff.Name + (sniff.Confidence == SniffConfidence.High ? string.Empty : " (đoán)");
    }

    protected override RewriteOptions BuildOptions(IReadOnlyList<string> files, string? outputFolder) => new()
    {
        Files = files,
        OutputFolder = outputFolder,
        Encoding = Output.Encoding,
        Newline = Output.Newline,
        SourceEncoding = SourceEncodingIndex switch
        {
            1 => new UTF8Encoding(false),
            2 => TextEncodings.ShiftJis,
            3 => new UnicodeEncoding(false, true),
            4 => Encoding.GetEncoding(1258),
            _ => null,
        },
    };
}

/// <summary>Trang "Đổi xuống dòng" (hàng loạt): CRLF ↔ LF, giữ nguyên encoding của từng file.</summary>
public sealed partial class NewlineViewModel : BatchViewModel
{
    /// <summary>0 = CRLF, 1 = LF.</summary>
    [ObservableProperty]
    private int _targetIndex;

    protected override string Title => "Đổi xuống dòng";

    protected override string Describe(string path) => SampleNewlines(path);

    protected override RewriteOptions BuildOptions(IReadOnlyList<string> files, string? outputFolder) => new()
    {
        Files = files,
        OutputFolder = outputFolder,
        Encoding = OutputEncoding.SameAsSource,
        Newline = TargetIndex == 0 ? NewlineMode.CrLf : NewlineMode.Lf,
    };
}
