using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModuleC.Models;
using ModuleC.Services;
using ModuleC.ViewModels;

namespace ModuleC;

public sealed partial class MainWindow : Window
{
    private readonly TableColumnCatalog _catalog = new();

    public SearchViewModel ViewModel { get; }
    public TableNameSearchViewModel TableNameSearchViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();

        ViewModel = new SearchViewModel(_catalog);
        TableNameSearchViewModel = new TableNameSearchViewModel(_catalog);

        _ = ViewModel.InitializeAsync();
    }

    private void RemoveRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AliasRow row })
        {
            ViewModel.RemoveRowCommand.Execute(row);
        }
    }

    private async void OpenTableNameSearch_Click(object sender, RoutedEventArgs e)
    {
        TableNameSearchDialog.XamlRoot = Content.XamlRoot;
        await TableNameSearchDialog.ShowAsync();
    }

    private void CloseTableNameSearchDialog_Click(object sender, RoutedEventArgs e)
    {
        TableNameSearchDialog.Hide();
    }
}
