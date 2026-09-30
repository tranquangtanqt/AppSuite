using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTools.Core;

namespace FileTools.ViewModels;

/// <summary>
/// Thanh "Mẫu" ở đầu cửa sổ: lưu / nạp / xoá bộ tuỳ chọn của trang đang mở (<see cref="PresetStore"/> →
/// Data\Config\presets.json cạnh exe). Tuỳ chọn được chụp bằng reflection (<see cref="PresetMapper"/>) - trạng thái chạy
/// (tiến độ, kết quả...) không lưu.
/// </summary>
public sealed partial class PresetBarViewModel : ObservableObject
{
    /// <summary>Thuộc tính là trạng thái / hiển thị, không phải tuỳ chọn người dùng.</summary>
    private static readonly HashSet<string> Exclude =
    [
        "IsBusy", "IsIdle", "Progress", "Status", "ResultPath", "HasResult", "Summary", "SourceInfo", "LayoutInfo",
        "TypedPath", "IsRunning", "IsStopped", "SelectedColumnIndex", "DetectedDelimiter",
    ];

    private static readonly string[] Nested = ["Output"];

    /// <summary>Đường dẫn nguồn áp trước: đổi nguồn làm trang tự đặt lại đường dẫn kết quả mặc định.</summary>
    private static readonly string[] ApplyFirst = ["SourcePath", "SourceFolder", "PathA", "PathB", "Folders"];

    private readonly PresetStore _store;
    private object? _target;
    private string _key = string.Empty;

    public PresetBarViewModel(PresetStore? store = null) => _store = store ?? new PresetStore(PresetStore.DefaultPath);

    public ObservableCollection<string> Names { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand), nameof(DeleteCommand))]
    private string? _selectedName;

    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>Gắn vào ViewModel của trang vừa mở; <paramref name="key"/> = tên trang (mỗi trang 1 danh sách mẫu).</summary>
    public void Attach(object? target, string key)
    {
        _target = target;
        _key = key;
        Status = string.Empty;
        Reload();
    }

    private void Reload(string? select = null)
    {
        Names.Clear();
        foreach (var p in _store.List(_key))
        {
            Names.Add(p.Name);
        }
        SelectedName = select is not null && Names.Contains(select) ? select : null;
    }

    private bool HasSelection() => _target is not null && SelectedName is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Load()
    {
        var preset = _store.List(_key).FirstOrDefault(p => p.Name == SelectedName);
        if (preset is null || _target is null)
        {
            return;
        }
        int applied = PresetMapper.Apply(_target, preset.Values, ApplyFirst);
        Status = $"Đã nạp mẫu \"{preset.Name}\" ({applied} tuỳ chọn).";
    }

    public void SaveAs(string name)
    {
        if (_target is null || string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        try
        {
            _store.Save(_key, new Preset(name.Trim(), PresetMapper.Capture(_target, Exclude, Nested)));
            Reload(name.Trim());
            Status = $"Đã lưu mẫu \"{name.Trim()}\".";
        }
        catch (Exception ex)
        {
            Status = "Không lưu được mẫu: " + ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        if (SelectedName is { } name && _store.Delete(_key, name))
        {
            Reload();
            Status = $"Đã xoá mẫu \"{name}\".";
        }
    }
}
