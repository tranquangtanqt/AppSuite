using MainLauncher.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace MainLauncher.Controls;

/// <summary>Tóm tắt "Đang hiện 10 / 12" + nút "Ẩn / hiện" (flyout ô tích). Lựa chọn nằm ở DashboardViewModel (singleton) nên
/// Dashboard, Module List và Settings luôn giống nhau.</summary>
public sealed partial class ModuleVisibilityButton : UserControl
{
    public DashboardViewModel ViewModel { get; } = App.Services.GetRequiredService<DashboardViewModel>();

    public ModuleVisibilityButton()
    {
        InitializeComponent();
    }
}
