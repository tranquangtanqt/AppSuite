using MainLauncher.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace MainLauncher.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage()
    {
        ViewModel = App.Services.GetRequiredService<DashboardViewModel>();
        InitializeComponent();
    }

    /// <summary>Kéo thả xong (GridView đã đổi chỗ trong VisibleModules) → lưu thứ tự mới cho mọi module.</summary>
    private void ModuleGrid_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => ViewModel.CommitDragOrder();
}
