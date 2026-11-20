using DbTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DbTools.Views;

public sealed partial class DbInfoPage : Page
{
    public DbInfoViewModel ViewModel { get; } = new();

    public DbInfoPage()
    {
        InitializeComponent();
        ScrollFit.Attach(Scroller, Root, 560);
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DbInfoViewModel.DefinitionHighlight) && ViewModel.DefinitionHighlight is { } match)
            {
                // Chữ định nghĩa vừa đổi → chờ dựng xong rồi bôi đen chỗ khớp đầu tiên + cuộn tới đó
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => Highlight(match));
            }
        };
    }

    private void Highlight(Core.SourceMatch match)
    {
        // Chữ trong TextBox dùng \r thay \n: độ dài giữ nguyên nên vị trí vẫn đúng
        if (match.Index + match.Length > DefinitionBox.Text.Length)
        {
            return;
        }
        DefinitionBox.Select(match.Index, match.Length);
        if (DefinitionBox.FindDescendant<ScrollViewer>() is { } viewer)
        {
            var rect = DefinitionBox.GetRectFromCharacterIndex(match.Index, false);
            viewer.ChangeView(null, Math.Max(0, viewer.VerticalOffset + rect.Top - 60), null, disableAnimation: true);
        }
    }

    private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.SearchCommand.CanExecute(null))
        {
            e.Handled = true;
            await ViewModel.SearchCommand.ExecuteAsync(null);
        }
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.EnsureProfile();
    }

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) =>
        ViewModel.Tab = sender.Items.IndexOf(sender.SelectedItem);

    private void DetailTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) =>
        ViewModel.DetailTab = sender.Items.IndexOf(sender.SelectedItem);

    private void ObjectKinds_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) =>
        ViewModel.ObjectKind = sender.Items.IndexOf(sender.SelectedItem);

    private void MockData_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.LoadedProfile is { } profile)
        {
            MockDataViewModel.Pending = (profile, ViewModel.LoadedDatabase, ViewModel.DetailTitle);
            App.MainWindow?.ShowPage("MockDataPage");
        }
    }

    private void DbBox_Loaded(object sender, RoutedEventArgs e) => Ui.RefreshEditableText(sender, e);

    private async void DbBox_DropDownOpened(object sender, object e) => await ViewModel.LoadDatabasesAsync();

    private async void TableList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.OpenDetailCommand.CanExecute(null))
        {
            await ViewModel.OpenDetailCommand.ExecuteAsync(null);
        }
    }

    private async void TableList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.OpenDetailCommand.CanExecute(null))
        {
            e.Handled = true;
            await ViewModel.OpenDetailCommand.ExecuteAsync(null);
        }
    }
}
