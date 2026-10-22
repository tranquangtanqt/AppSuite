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
        Closed += (_, _) => _helpWindow?.Close(); // đóng launcher thì đóng luôn cửa sổ Hướng dẫn
        // Trang dựng lỗi (vd lỗi XAML) → ghi lại lỗi thay vì chỉ thấy app tắt.
        ContentFrame.NavigationFailed += (_, e) =>
            App.Services.GetService<ILogger<MainWindow>>()?.LogError(e.Exception, "Không mở được trang {Page}", e.SourcePageType?.Name);
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(DashboardPage));
    }

    // ----- Hướng dẫn (F1) -----

    private SharedUI.Help.HelpWindow? _helpWindow;

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if ((args.InvokedItemContainer as NavigationViewItem)?.Tag as string == "Help")
            ShowHelp();
    }

    private void HelpAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ShowHelp();
    }

    /// <summary>Mở cửa sổ Hướng dẫn (1 cửa sổ duy nhất - đang mở thì đưa lên trước). Nội dung: Views/HelpContent.</summary>
    private void ShowHelp()
    {
        if (_helpWindow is null)
        {
            _helpWindow = new SharedUI.Help.HelpWindow("AppSuite - Main Launcher", "Mở / đóng các công cụ của AppSuite, ẩn / hiện module, xem nhật ký.",
                HelpContent.Sections, searchPlaceholder: "Tìm tính năng, vd: Stop, ẩn module, modules.json...");
            _helpWindow.Closed += (_, _) => _helpWindow = null;
        }
        _helpWindow.Activate();
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
