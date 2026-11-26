using DbTools.Core;
using DbTools.Models;
using DbTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DbTools.Views;

public sealed partial class ServerLogPage : Page
{
    public ServerLogViewModel ViewModel { get; } = new();

    /// <summary>Trước khi Theo dõi thêm dòng: người dùng đang ở cuối bảng không (đang cuộn lên xem dòng cũ thì không kéo xuống).</summary>
    private bool _atBottom = true;

    public ServerLogPage()
    {
        InitializeComponent();
        foreach (var label in ServerLogViewModel.ReadSizeLabels)
        {
            SizeBox.Items.Add(label);
        }
        SizeBox.SelectedIndex = ViewModel.ReadSizeIndex;
        ViewModel.Confirm = c => Dialogs.ConfirmAsync(XamlRoot, c);
        ViewModel.ChooseConfig = ChooseConfigAsync;
        // "Cuối" = phía dòng mới nhất: cuối bảng khi xếp cũ trước, đầu bảng khi xếp mới trước.
        ViewModel.RowsAppending += (_, _) => _atBottom = ListScroll() is not { } sv
            || (ViewModel.NewestFirst ? sv.VerticalOffset <= 40 : sv.VerticalOffset >= sv.ScrollableHeight - 40);
        ViewModel.RowsAppended += (_, _) =>
        {
            if (_atBottom)
            {
                ScrollToNewest();
            }
        };
        ViewModel.ScrollToEndRequested += (_, _) =>
        {
            _atBottom = true;
            ScrollToNewest();
        };
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.EnsureProfile();
    }

    /// <summary>Rời trang thì thôi theo dõi (không giữ kết nối tới server khi không xem).</summary>
    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.StopFollow();
    }

    private void EditConfig_Click(object sender, RoutedEventArgs e)
    {
        ConfigFlyout.Hide();
        if (ViewModel.EditConfigCommand.CanExecute(null))
        {
            ViewModel.EditConfigCommand.Execute(null);
        }
    }

    /// <summary>Hộp chọn giá trị mới cho 4 thông số log (Tiếp = đổi, Về mặc định = RESET các thông số đặt bằng ALTER SYSTEM).</summary>
    private async Task<LogConfigChoice?> ChooseConfigAsync(ConnectionProfile profile, IReadOnlyDictionary<string, LogSetting> settings)
    {
        var current = LogConfigValues.From(settings);
        var statementBox = new ComboBox { Header = "Ghi câu lệnh (log_statement)", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var v in LogConfigEdit.StatementValues)
        {
            statementBox.Items.Add($"{v} - {LogConfigEdit.StatementText(v)}");
        }
        statementBox.SelectedIndex = Math.Max(0, Array.IndexOf(LogConfigEdit.StatementValues, current.Statement));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(statementBox, "LogStatementBox");

        var durationBox = new NumberBox
        {
            Header = "Ghi câu chạy lâu hơn (ms)",
            Value = current.MinDurationMs,
            Minimum = -1,
            SmallChange = 100,
            LargeChange = 1000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(durationBox, "LogMinDurationBox");
        var connections = new CheckBox { Content = "Ghi kết nối mới (log_connections)", IsChecked = current.Connections };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(connections, "LogConnectionsBox");
        var disconnections = new CheckBox { Content = "Ghi ngắt kết nối + thời gian phiên (log_disconnections)", IsChecked = current.Disconnections };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(disconnections, "LogDisconnectionsBox");

        var panel = new StackPanel { Spacing = 10, MaxWidth = 560 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Server {profile.Host}:{profile.Port}: áp cho cả server (mọi database), ghi vào postgresql.auto.conf trên máy server rồi nạp lại cấu hình - không cần khởi động lại. Kết nối mới mới có log_connections / log_disconnections.",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(statementBox);
        panel.Children.Add(durationBox);
        panel.Children.Add(new TextBlock { Text = "log_min_duration_statement: -1 = tắt, 0 = mọi câu kèm thời gian chạy.", FontSize = 12, Opacity = 0.75, Margin = new Thickness(0, -6, 0, 0) });
        panel.Children.Add(connections);
        panel.Children.Add(disconnections);
        var sources = string.Join(Environment.NewLine, LogConfigEdit.Names.Where(settings.ContainsKey).Select(n => settings[n])
            .Select(s => $"{s.Name} = {s.Setting} · {(s.ByAlterSystem ? "đặt bằng ALTER SYSTEM" : s.Source == "default" ? "mặc định" : s.Source)}"));
        panel.Children.Add(new TextBlock { Text = "Đang áp:" + Environment.NewLine + sources, FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });

        var canReset = settings.Values.Any(s => s.ByAlterSystem);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Đổi cấu hình log",
            Content = new ScrollViewer { Content = panel, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = "Tiếp",
            SecondaryButtonText = "Về mặc định",
            IsSecondaryButtonEnabled = canReset,
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Close,
        };
        var result = await dialog.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary => new LogConfigChoice(new LogConfigValues(
                LogConfigEdit.StatementValues[Math.Max(0, statementBox.SelectedIndex)],
                double.IsNaN(durationBox.Value) ? current.MinDurationMs : (int)Math.Clamp(Math.Round(durationBox.Value), -1, int.MaxValue),
                connections.IsChecked == true,
                disconnections.IsChecked == true), false),
            ContentDialogResult.Secondary => new LogConfigChoice(null, true),
            _ => null,
        };
    }

    private void ScrollToNewest()
    {
        if (ViewModel.Rows.Count > 0)
        {
            LogList.ScrollIntoView(ViewModel.NewestFirst ? ViewModel.Rows[0] : ViewModel.Rows[^1]);
        }
    }

    private ScrollViewer? ListScroll() => FindChild<ScrollViewer>(LogList);

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found)
            {
                return found;
            }
            if (FindChild<T>(child) is { } deeper)
            {
                return deeper;
            }
        }
        return null;
    }
}
