using FileTools.Services;
using FileTools.ViewModels;
using FileTools.Views;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;

namespace FileTools;

public sealed partial class MainWindow : Window
{
    public PresetBarViewModel Presets { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1280, 880));
        AppLog.For("App").LogInformation("Khởi động FileTools");
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First();
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag } item
            && Type.GetType($"FileTools.Views.{tag}") is { } pageType
            && ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
            sender.Header = null;
            item.IsSelected = true;
        }
    }

    /// <summary>Thanh Mẫu gắn vào ViewModel của trang vừa mở (mọi trang đều có thuộc tính ViewModel).</summary>
    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        var page = ContentFrame.Content;
        var viewModel = page?.GetType().GetProperty("ViewModel")?.GetValue(page);
        Presets.Attach(viewModel, page?.GetType().Name ?? string.Empty);
    }

    private HelpWindow? _help;

    private void Nav_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem { Tag: "Help" })
        {
            OpenHelp();
        }
    }

    private void Help_Click(object sender, RoutedEventArgs e) => OpenHelp();

    /// <summary>Mở (hoặc đưa lên trước) cửa sổ Hướng dẫn ở đúng mục của trang đang xem.</summary>
    private void OpenHelp()
    {
        var page = ContentFrame.Content?.GetType().Name;
        if (_help is null)
        {
            _help = new HelpWindow(page);
            _help.Closed += (_, _) => _help = null;
        }
        else
        {
            _help.ShowPage(page);
        }
        _help.Activate();
    }

    private async void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        var box = new TextBox { PlaceholderText = "vd: Log hằng ngày 100 MB", Text = Presets.SelectedName ?? string.Empty };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, "Tên mẫu");
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Lưu mẫu cho trang này",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Lưu toàn bộ tuỳ chọn đang chọn trên trang (không lưu danh sách file). Trùng tên thì ghi đè.", TextWrapping = TextWrapping.Wrap },
                    box,
                },
            },
            PrimaryButtonText = "Lưu",
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text))
        {
            Presets.SaveAs(box.Text);
        }
    }
}
