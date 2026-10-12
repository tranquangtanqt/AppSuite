using MeRon.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MeRon.Views;

public sealed partial class ReportSummaryPage : Page
{
    public ReportSummaryViewModel ViewModel { get; } = new();

    public ReportSummaryPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.NavigationMode == NavigationMode.New)
        {
            ViewModel.Range.Reset();
        }
        ViewModel.Load();
    }
}
