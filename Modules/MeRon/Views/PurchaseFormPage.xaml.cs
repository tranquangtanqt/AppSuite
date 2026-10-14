using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MeRon.Views;

public sealed partial class PurchaseFormPage : Page
{
    public PurchaseFormViewModel ViewModel { get; } = new();

    public PurchaseFormPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var args = e.Parameter as PurchaseFormArgs ?? new PurchaseFormArgs(null);
        ViewModel.Load(args.Id, args.InitialDay);
        Bindings.Update();
    }

    // Tổng tính lại ngay lúc gõ (LiveNumberBox); tiền canh phải.
    private void PriceBox_Loaded(object sender, RoutedEventArgs e)
    {
        var box = (NumberBox)sender;
        box.NumberFormatter = Format.MoneyFormatter();
        if (box.DataContext is PurchaseLineViewModel line)
        {
            LiveNumberBox.Attach(box, line.PreviewPrice, TextAlignment.Right);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => GoBack();

    private void GoBack()
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
        else
        {
            Frame.Navigate(typeof(PurchasesPage));
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        (sender as Control)?.Focus(FocusState.Programmatic); // Ctrl+S: ô đang gõ ghi nhận giá trị trước
        if (ViewModel.Validate() is { } invalid)
        {
            if (invalid.LineIndex is { } index && LinesList.ContainerFromIndex(index) is DependencyObject row
                && Ui.FindByName(row, invalid.PriceField ? "PriceBox" : "NameBox") is Control control)
            {
                control.StartBringIntoView();
                control.Focus(FocusState.Programmatic);
            }
            return;
        }
        if (ViewModel.Save())
        {
            GoBack();
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (await Dialogs.ConfirmAsync(XamlRoot, "Xoá phiếu nhập?", "Xoá phiếu nhập và toàn bộ khoản bên trong. Không hoàn tác được.", "Xoá")
            && ViewModel.Delete())
        {
            GoBack();
        }
    }
}
