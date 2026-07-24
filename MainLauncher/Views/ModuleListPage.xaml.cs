using MainLauncher.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace MainLauncher.Views;

public sealed partial class ModuleListPage : Page
{
    public ModuleListViewModel ViewModel { get; }

    public ModuleListPage()
    {
        ViewModel = App.Services.GetRequiredService<ModuleListViewModel>();
        InitializeComponent();
    }
}
