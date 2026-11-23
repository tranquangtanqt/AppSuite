using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DbTools.Models;
using DbTools.Services;

namespace DbTools.ViewModels;

/// <summary>Trang "Kết nối": danh sách hồ sơ + ô sửa hồ sơ đang chọn (sửa là tự lưu) + Thử kết nối.</summary>
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

    /// <summary>Trang gắn vào: hỏi lại trước khi xoá hồ sơ.</summary>
    public Func<string, Task<bool>>? ConfirmDelete { get; set; }

    public ConnectionsViewModel()
    {
        Selected = Profiles.FirstOrDefault(p => p.Name == AppState.Settings.LastProfile) ?? Profiles.FirstOrDefault();
        Profiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
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
        var profile = new ConnectionProfile { Name = UniqueName("Kết nối mới") };
        Profiles.Add(profile);
        Selected = profile;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        var copy = Selected!.Clone(UniqueName(Selected.Name + " (bản sao)"));
        Profiles.Insert(Profiles.IndexOf(Selected) + 1, copy);
        Selected = copy;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        var profile = Selected!;
        if (ConfirmDelete is not null && !await ConfirmDelete($"Xoá hồ sơ kết nối \"{profile.Display}\"?"))
        {
            return;
        }
        var index = Profiles.IndexOf(profile);
        Profiles.Remove(profile);
        Selected = Profiles.Count == 0 ? null : Profiles[Math.Min(index, Profiles.Count - 1)];
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
