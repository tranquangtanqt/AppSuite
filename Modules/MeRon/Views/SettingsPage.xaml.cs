using MeRon.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MeRon.Views;

/// <summary>Cài đặt: ẩn / hiện từng mục menu trái. Đổi ô tích là lưu + áp dụng lên menu ngay.</summary>
public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private static MainWindow? Window => App.MainWindow;

    protected override void OnNavigatedTo(NavigationEventArgs e) => Build();

    /// <summary>Mỗi nhóm menu (Bán hàng, Thống kê...) 1 tiêu đề + các ô tích, xếp ngang tự xuống dòng.</summary>
    private void Build()
    {
        MenuGroups.Children.Clear();
        if (Window is not { } window)
        {
            return;
        }
        foreach (var group in window.MenuEntries().GroupBy(m => m.Group))
        {
            var boxes = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 220 };
            foreach (var entry in group)
            {
                var locked = MenuSettings.AlwaysVisible.Contains(entry.Page);
                var box = new CheckBox
                {
                    Content = locked ? $"{entry.Name} (luôn hiện)" : entry.Name,
                    Tag = entry.Page,
                    IsChecked = window.MenuSettings.IsVisible(entry.Page),
                    IsEnabled = !locked,
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, entry.Name);
                box.Checked += Box_Changed;
                box.Unchecked += Box_Changed;
                boxes.Children.Add(box);
            }
            var panel = new StackPanel { Spacing = 2 };
            panel.Children.Add(new TextBlock { Text = group.Key, Opacity = 0.7, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] });
            panel.Children.Add(boxes);
            MenuGroups.Children.Add(panel);
        }
        ShowStatus();
    }

    private void Box_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: string page } box && Window is { } window)
        {
            window.SetMenuVisible(page, box.IsChecked == true);
            ShowStatus();
        }
    }

    private void ShowAll_Click(object sender, RoutedEventArgs e)
    {
        Window?.ShowAllMenu();
        Build();
    }

    private void ShowStatus()
    {
        var hidden = Window?.MenuSettings.Hidden.Count ?? 0;
        MenuStatus.Text = hidden == 0 ? "Đang hiện tất cả các mục." : $"Đang ẩn {hidden} mục.";
    }
}
