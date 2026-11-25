using DbTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DbTools.Views;

public sealed partial class MockDataPage : Page
{
    public MockDataViewModel ViewModel { get; } = new();

    public MockDataPage()
    {
        InitializeComponent();
        Ui.KeepTextIfNothingPicked(DbBox, TableBox);
        ScrollFit.Attach(Scroller, Root, 640);
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.EnsureProfile();
        if (MockDataViewModel.Pending is not null)
        {
            await ViewModel.TakePendingAsync();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                Ui.RefreshEditableText(DbBox, new RoutedEventArgs());
                Ui.RefreshEditableText(TableBox, new RoutedEventArgs());
            });
        }
    }

    private void EditableBox_Loaded(object sender, RoutedEventArgs e) => Ui.RefreshEditableText(sender, e);

    private async void DbBox_DropDownOpened(object sender, object e) => await ViewModel.LoadDatabasesAsync();

    private async void TableBox_DropDownOpened(object sender, object e) => await ViewModel.LoadTableNamesAsync();
}
