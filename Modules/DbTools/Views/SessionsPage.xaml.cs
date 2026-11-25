using DbTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DbTools.Views;

public sealed partial class SessionsPage : Page
{
    public SessionsViewModel ViewModel { get; } = new();

    public SessionsPage()
    {
        InitializeComponent();
        Ui.KeepTextIfNothingPicked(DbBox);
        ViewModel.Confirm = c => Dialogs.ConfirmAsync(XamlRoot, c);
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.EnsureProfile();
    }

    private void SessionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
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
