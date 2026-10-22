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

    [RelayCommand]
    private Task ReloadModules() => _moduleList.LoadModulesAsync();
}
