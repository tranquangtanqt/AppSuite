using DbTools.Core;
using DbTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DbTools.Views;

public sealed partial class SessionsPage : Page
{
    private bool _reselecting;

    public SessionsViewModel ViewModel { get; } = new();

    public SessionsPage()
    {
        InitializeComponent();
        Ui.KeepTextIfNothingPicked(DbBox);
        ViewModel.Confirm = c => Dialogs.ConfirmAsync(XamlRoot, c);
        // Làm mới (kể cả tự làm mới) dựng lại danh sách: chọn lại đúng các PID đang chọn.
        ViewModel.Reloaded += (_, pids) =>
        {
            _reselecting = true;
            try
            {
                foreach (var row in ViewModel.Rows.Where(r => pids.Contains(r.Pid)))
                {
                    SessionList.SelectedItems.Add(row);
                }
            }
            finally
            {
                _reselecting = false;
            }
            SyncSelection();
        };
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.EnsureProfile();
    }

    /// <summary>Rời trang thì thôi tự làm mới (không đọc server khi không xem).</summary>
    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Leave();
    }

    public static Brush BlockBrush(BlockState state) => state switch
    {
        BlockState.Blocked => Ui.ResultBrush(false),
        BlockState.Blocking => Ui.FolderBrush,
        _ => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
    };

    private void SessionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_reselecting)
        {
            SyncSelection();
        }
    }

    private void SyncSelection()
    {
        ViewModel.Selected.Clear();
        ViewModel.Selected.AddRange(SessionList.SelectedItems.OfType<SessionRow>());
        ViewModel.SelectionChanged();
    }

    private void DbBox_Loaded(object sender, RoutedEventArgs e) => Ui.RefreshEditableText(sender, e);

    private async void DbBox_DropDownOpened(object sender, object e) => await ViewModel.LoadDatabasesAsync();

    /// <summary>ComboBox gõ được không đẩy chữ rỗng qua binding → chốt tay khi Enter / rời ô (như ô Schema trang Chạy).</summary>
    private void DbBox_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args) => ViewModel.Database = args.Text.Trim();

    private void DbBox_LostFocus(object sender, RoutedEventArgs e) => ViewModel.Database = DbBox.Text.Trim();

    private void ClearDb_Click(object sender, RoutedEventArgs e)
    {
        DbBox.SelectedItem = null;
        DbBox.Text = string.Empty;
        ViewModel.Database = string.Empty;
    }
}
