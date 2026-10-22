using MainLauncher.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace MainLauncher.Views;

public sealed partial class ModuleListPage : Page
{
    public ModuleListViewModel ViewModel { get; }

    /// <summary>Module đang hiện (theo lựa chọn Ẩn / hiện chung với Dashboard).</summary>
    public DashboardViewModel Shown { get; }

    public ModuleListPage()
    {
        ViewModel = App.Services.GetRequiredService<ModuleListViewModel>();
        Shown = App.Services.GetRequiredService<DashboardViewModel>();
        InitializeComponent();
    }
}
