using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;

namespace MainLauncher.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ModuleListViewModel _moduleList;

    private readonly MainLauncher.Services.LauncherSettingsStore _settingsStore;

    public SettingsViewModel(IConfiguration configuration, ModuleListViewModel moduleList, DashboardViewModel dashboard,
        MainLauncher.Services.LauncherSettingsStore settingsStore)
    {
        _moduleList = moduleList;
        _settingsStore = settingsStore;
        Dashboard = dashboard;
        BaseDirectory = AppContext.BaseDirectory;
        ModulesConfigPath = configuration["ModulesConfigPath"] ?? "Config/modules.json";
    }

    /// <summary>Ẩn / hiện module trên Dashboard - cùng danh sách với nút "Ẩn / hiện" trên Dashboard.</summary>
    public DashboardViewModel Dashboard { get; }

    public string BaseDirectory { get; }

    public string ModulesConfigPath { get; }

    /// <summary>RadioButtons "Khi đóng MainLauncher": 0 = Hỏi, 1 = Đóng hết, 2 = Để chạy tiếp (thứ tự = enum CloseModulesOnExit).
    /// Đổi là lưu ngay; cũng đổi khi tích "Nhớ lựa chọn" trong hộp thoại lúc đóng.</summary>
    public int CloseModulesOnExitIndex
    {
        get => (int)_settingsStore.Current.CloseModulesOnExit;
        set
        {
            if (value < 0 || value == (int)_settingsStore.Current.CloseModulesOnExit)
                return;
            _settingsStore.Current.CloseModulesOnExit = (MainLauncher.Services.CloseModulesOnExit)value;
            _settingsStore.Save();
            OnPropertyChanged();
        }
    }

    // ----- Khởi động cùng Windows (registry HKCU Run, xem Services\StartupRegistration) -----

    private bool _startWithWindows;

    /// <summary>ToggleSwitch "Khởi động cùng Windows": bật = mục Run trỏ đúng exe này và không bị tắt ở Task Manager.</summary>
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (value == _startWithWindows)
                return;
            try
            {
                if (value)
                    MainLauncher.Services.StartupRegistration.Enable();
                else
                    MainLauncher.Services.StartupRegistration.Disable();
            }
            catch (Exception ex)
            {
                StartupStatus = $"Không ghi được registry: {ex.Message}";
                OnPropertyChanged(); // trả công tắc về trạng thái cũ
                return;
            }
            RefreshStartup();
        }
    }

    /// <summary>Dòng giải thích dưới công tắc (trạng thái thật trong registry).</summary>
    [ObservableProperty]
    private string _startupStatus = "";

    /// <summary>"Mở thu nhỏ xuống thanh tác vụ" khi Windows mở lúc đăng nhập - lưu launcher-settings.json.</summary>
    public bool StartMinimized
    {
        get => _settingsStore.Current.StartMinimized;
        set
        {
            if (value == _settingsStore.Current.StartMinimized)
                return;
            _settingsStore.Current.StartMinimized = value;
            _settingsStore.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>"Khi bấm X: thu xuống khay hệ thống" - lưu launcher-settings.json; cửa sổ chính bật / tắt icon khay theo.</summary>
    public bool CloseToTray
    {
        get => _settingsStore.Current.CloseToTray;
        set
        {
            if (value == _settingsStore.Current.CloseToTray)
                return;
            _settingsStore.Current.CloseToTray = value;
            _settingsStore.Save();
            OnPropertyChanged();
        }
    }

    public void RefreshStartup()
    {
        var (state, path) = MainLauncher.Services.StartupRegistration.GetState();
        _startWithWindows = state == MainLauncher.Services.StartupState.On;
        OnPropertyChanged(nameof(StartWithWindows));
        StartupStatus = state switch
        {
            MainLauncher.Services.StartupState.On =>
                "Đăng nhập Windows là MainLauncher tự mở (chỉ tài khoản Windows này). Module không tự chạy - bấm Chạy như thường.",
            MainLauncher.Services.StartupState.OtherPath =>
                $"Đang đăng ký cho MainLauncher ở chỗ khác: {path}. Bật công tắc để dùng bản này ({MainLauncher.Services.StartupRegistration.ExePath}).",
            MainLauncher.Services.StartupState.DisabledByUser =>
                "Đã bị tắt ở Task Manager > Startup apps (hoặc Settings > Apps > Startup). Bật công tắc để bật lại.",
            _ => "Tắt: đăng nhập Windows xong phải tự mở MainLauncher.",
        };
    }

    [RelayCommand]
    private Task ReloadModules() => _moduleList.LoadModulesAsync();

    /// <summary>"Sắp xếp lại theo modules.json": bỏ thứ tự đã kéo thả.</summary>
    public IRelayCommand ResetOrderCommand => _moduleList.ResetOrderCommand;
}
