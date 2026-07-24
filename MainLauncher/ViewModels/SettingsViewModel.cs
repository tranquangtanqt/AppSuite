using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;

namespace MainLauncher.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ModuleListViewModel _moduleList;

    public SettingsViewModel(IConfiguration configuration, ModuleListViewModel moduleList)
    {
        _moduleList = moduleList;
        BaseDirectory = AppContext.BaseDirectory;
        ModulesConfigPath = configuration["ModulesConfigPath"] ?? "Config/modules.json";
    }

    public string BaseDirectory { get; }

    public string ModulesConfigPath { get; }

    [RelayCommand]
    private Task ReloadModules() => _moduleList.LoadModulesAsync();
}
