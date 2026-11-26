using DbTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace DbTools.Views;

public sealed partial class ServerConfigPage : Page
{
    public ServerConfigViewModel ViewModel { get; } = new();

    public ServerConfigPage()
    {
        InitializeComponent();
        ViewModel.Confirm = c => Dialogs.ConfirmAsync(XamlRoot, c);
        // Chọn thông số khác: ô chi tiết về đầu (không giữ chỗ cuộn của thông số trước).
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ServerConfigViewModel.Selected))
            {
                DetailScroll.ChangeView(null, 0, null, true);
            }
        };
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.EnsureProfile();
        if (ServerConfigViewModel.Request is not null)
        {
            await ViewModel.TakeRequestAsync(); // lối tắt từ Nhật ký server: mở sẵn nhóm Log
        }
    }

    public Brush StatusBrush(bool error) => error ? Ui.ResultBrush(false) : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];

    public Visibility EmptyVisible(string countText) => Ui.VisibleIf(string.IsNullOrEmpty(countText));

    /// <summary>Enter trong ô giá trị = Đổi (đưa vào danh sách chờ).</summary>
    private void EditBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && ViewModel.StageCommand.CanExecute(null))
        {
            e.Handled = true;
            ViewModel.StageCommand.Execute(null);
        }
    }
}
