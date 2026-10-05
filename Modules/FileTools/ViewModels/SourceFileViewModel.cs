using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTools.Core;
using FileTools.Services;

namespace FileTools.ViewModels;

/// <summary>Trang làm việc trên 1 file nguồn: chọn / kéo-thả file, hiện dung lượng + encoding nhận được.</summary>
public abstract partial class SourceFileViewModel : JobViewModel, IUsesSharedFile
{
    [ObservableProperty]
    private string _sourcePath = string.Empty;

    [ObservableProperty]
    private string _sourceInfo = "Chưa chọn file.";

    public bool SourceIsCsv => Csv.IsCsvPath(SourcePath);

    [RelayCommand]
    private async Task BrowseSource()
    {
        if (await Pickers.PickFileAsync() is { } path)
        {
            SourcePath = path;
        }
    }

    partial void OnSourcePathChanged(string value)
    {
        SharedFile.Set(value);
        OnSourceChanged(value);
        _ = RefreshInfoAsync(value);
    }

    public void ApplySharedFile(string path)
    {
        if (!IsBusy && !SharedFile.SamePath(path, SourcePath))
        {
            SourcePath = path;
        }
    }

    /// <summary>Đặt giá trị mặc định theo file mới (thư mục đầu ra, lặp tiêu đề CSV...).</summary>
    protected virtual void OnSourceChanged(string path)
    {
    }

    private async Task RefreshInfoAsync(string path)
    {
        if (!File.Exists(path))
        {
            SourceInfo = string.IsNullOrWhiteSpace(path) ? "Chưa chọn file." : "Không tìm thấy file.";
            return;
        }
        try
        {
            var (length, sniff) = await Task.Run(() => (new FileInfo(path).Length, EncodingSniffer.Detect(path)));
            if (path == SourcePath)
            {
                SourceInfo = $"{FileItem.FormatSize(length)} · {sniff.Name}" + (sniff.Warning is null ? string.Empty : $" · {sniff.Warning}");
            }
        }
        catch (Exception ex)
        {
            SourceInfo = "Không đọc được: " + ex.Message;
        }
    }

    protected bool HasSource() => File.Exists(SourcePath);
}
