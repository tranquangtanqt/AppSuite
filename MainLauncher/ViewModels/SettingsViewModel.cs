using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;

namespace MainLauncher.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ModuleListViewModel _moduleList;

    public SettingsViewModel(IConfiguration configuration, ModuleListViewModel moduleList, DashboardViewModel dashboard)
    {
        _moduleList = moduleList;
        Dashboard = dashboard;
        BaseDirectory = AppContext.BaseDirectory;
        ModulesConfigPath = configuration["ModulesConfigPath"] ?? "Config/modules.json";
    }

    /// <summary>Ẩn / hiện module trên Dashboard - cùng danh sách với nút "Ẩn / hiện" trên Dashboard.</summary>
    public DashboardViewModel Dashboard { get; }

    public string BaseDirectory { get; }

    public string ModulesConfigPath { get; }

    [RelayCommand]
    private Task ReloadModules() => _moduleList.LoadModulesAsync();
}
