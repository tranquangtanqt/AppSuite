using System;
using MainLauncher.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MainLauncher;

/// <summary>Application shell: a NavigationView hosting Dashboard / Module List / Logs / Settings.</summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SharedUI.Helpers.WindowIcon.Apply(this);
        // Trang dựng lỗi (vd lỗi XAML) → ghi lại lỗi thay vì chỉ thấy app tắt.
        ContentFrame.NavigationFailed += (_, e) =>
            App.Services.GetService<ILogger<MainWindow>>()?.LogError(e.Exception, "Không mở được trang {Page}", e.SourcePageType?.Name);
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(DashboardPage));
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
        var pageType = tag switch
        {
            "Dashboard" => typeof(DashboardPage),
            "Modules" => typeof(ModuleListPage),
            "Logs" => typeof(LogsPage),
            "Settings" => typeof(SettingsPage),
            _ => typeof(DashboardPage)
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            try
            {
                ContentFrame.Navigate(pageType);
            }
            catch (Exception ex)
            {
                App.Services.GetService<ILogger<MainWindow>>()?.LogError(ex, "Không mở được trang {Page}", pageType.Name);
            }
        }
    }
}
