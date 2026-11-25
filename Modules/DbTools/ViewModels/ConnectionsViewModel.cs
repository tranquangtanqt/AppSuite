using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DbTools.Core;
using DbTools.Models;
using DbTools.Services;

namespace DbTools.ViewModels;

/// <summary>Trang "Kết nối": cây hồ sơ theo thư mục + ô sửa hồ sơ đang chọn (sửa là tự lưu) + Thử kết nối.</summary>
public sealed partial class ConnectionsViewModel : ObservableObject
{
    public ObservableCollection<ConnectionProfile> Profiles => AppState.Profiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectedPort))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateCommand), nameof(DeleteCommand), nameof(TestCommand))]
    private ConnectionProfile? _selected;

    /// <summary>NumberBox dùng double.</summary>
    public double SelectedPort
    {
        get => Selected?.Port ?? 5432;
        set
        {
            if (Selected is not null && !double.IsNaN(value) && value is >= 1 and <= 65535)
            {
                Selected.Port = (int)value;
            }
        }
    }

    [ObservableProperty]
    private string _testResult = string.Empty;

    [ObservableProperty]
    private bool _testFailed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    private bool _isTesting;

    public bool HasSelection => Selected is not null;

    public bool IsEmpty => Profiles.Count == 0;

    /// <summary>Ô tìm: chỉ hiện hồ sơ khớp.</summary>
    [ObservableProperty]
    private string _filter = string.Empty;

    /// <summary>Ô tìm có chữ nhưng không hồ sơ nào khớp.</summary>
    public bool NoMatch => !IsEmpty && !Profiles.Any(p => ProfileTree.Matches(p, Filter));

    /// <summary>Thư mục đang bấm chọn trong cây (khi không chọn hồ sơ nào) - Thêm vào đây.</summary>
    public string SelectedFolder { get; private set; } = string.Empty;

    /// <summary>Cây phải dựng lại (thêm / xoá / chuyển thư mục / đổi ô tìm). Trang gắn vào.</summary>
    public event Action? TreeChanged;

    partial void OnFilterChanged(string value) => RaiseTreeChanged();

    public void RaiseTreeChanged()
    {
        OnPropertyChanged(nameof(NoMatch));
        TreeChanged?.Invoke();
    }

    public void SelectFolder(string path)
    {
        Selected = null;
        SelectedFolder = path;
    }

    /// <summary>Chuyển hồ sơ sang thư mục khác (kéo-thả, ô Thư mục).</summary>
    public void MoveProfile(ConnectionProfile profile, string folder)
    {
        profile.Folder = ProfileTree.MatchExisting(Profiles.Where(p => p != profile), folder);
        RaiseTreeChanged();
    }

    /// <summary>Đổi tên / chuyển chỗ thư mục (kéo-thả, menu Đổi tên thư mục).</summary>
    public void MoveFolder(string oldPath, string newPath)
    {
        ProfileTree.MoveFolder(Profiles, oldPath, newPath);
        if (Selected is null)
        {
            SelectedFolder = ProfileTree.Normalize(newPath);
        }
        RaiseTreeChanged();
    }

    /// <summary>Trang gắn vào: hỏi lại trước khi xoá hồ sơ.</summary>
    public Func<string, Task<bool>>? ConfirmDelete { get; set; }

    public ConnectionsViewModel()
    {
        Selected = AppState.LastProfile;
        Profiles.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            RaiseTreeChanged();
        };
    }

    partial void OnSelectedChanged(ConnectionProfile? value)
    {
        _testCts?.Cancel();
        TestResult = string.Empty;
        TestFailed = false;
    }

    [RelayCommand]
    private void Add()
    {
        Filter = string.Empty; // đang tìm thì hồ sơ mới không khớp, không thấy đâu
        var profile = new ConnectionProfile { Name = UniqueName("Kết nối mới"), Folder = Selected?.Folder ?? SelectedFolder };
        Profiles.Add(profile);
        Selected = profile;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        Filter = string.Empty;
        var copy = Selected!.Clone(UniqueName(Selected.Name + " (bản sao)"));
        Profiles.Insert(Profiles.IndexOf(Selected) + 1, copy);
        Selected = copy;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        var profile = Selected!;
        if (ConfirmDelete is not null && !await ConfirmDelete($"Xoá hồ sơ kết nối \"{profile.PickerText}\"?"))
        {
            return;
        }
        var index = ProfileTree.Flatten(Profiles).IndexOf(profile);
        Profiles.Remove(profile);
        var rest = ProfileTree.Flatten(Profiles);
        SelectedFolder = string.Empty;
        Selected = rest.Count == 0 ? null : rest[Math.Min(index, rest.Count - 1)];
    }

    private bool CanTest => Selected is not null && !IsTesting;

    private CancellationTokenSource? _testCts;

    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestAsync()
    {
        var profile = Selected!;
        using var cts = new CancellationTokenSource();
        _testCts = cts;
        IsTesting = true;
        TestFailed = false;
        TestResult = $"Đang kết nối... (sai host / port hoặc tường lửa chặn thì chờ tới {Pg.ConnectTimeoutSeconds} giây - bấm Huỷ để thôi)";
        try
        {
            // WaitAsync: Huỷ là thôi chờ ngay, kể cả khi Npgsql còn kẹt ở bước mở socket (nó tự bỏ khi hết 15 giây).
            var info = await Pg.TestAsync(profile, cts.Token).WaitAsync(cts.Token);
            TestResult = "Kết nối được: " + info;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            if (Selected == profile)
            {
                TestResult = "Đã huỷ thử kết nối.";
            }
        }
        catch (Exception ex)
        {
            TestFailed = true;
            TestResult = "Không kết nối được: " + Pg.Describe(ex);
        }
        finally
        {
            _testCts = null;
            IsTesting = false;
        }
    }

    /// <summary>Thôi chờ Thử kết nối (nút Huỷ; đổi sang hồ sơ khác cũng huỷ).</summary>
    [RelayCommand]
    private void CancelTest() => _testCts?.Cancel();

    private string UniqueName(string baseName)
    {
        var name = baseName;
        for (var n = 2; Profiles.Any(p => p.Name == name); n++)
        {
            name = $"{baseName} {n}";
        }
        return name;
    }
}
