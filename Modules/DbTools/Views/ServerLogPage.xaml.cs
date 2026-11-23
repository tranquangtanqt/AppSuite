using DbTools.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace DbTools.Views;

public sealed partial class ServerLogPage : Page
{
    public ServerLogViewModel ViewModel { get; } = new();

    public ServerLogPage()
    {
        InitializeComponent();
        foreach (var label in ServerLogViewModel.ReadSizeLabels)
        {
            SizeBox.Items.Add(label);
        }
        SizeBox.SelectedIndex = ViewModel.ReadSizeIndex;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.EnsureProfile();
    }
}
