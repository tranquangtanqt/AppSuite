using DbTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DbTools.Views;

public sealed partial class ConnectionsPage : Page
{
    public ConnectionsViewModel ViewModel { get; } = new();

    private bool _settingPassword;

    public ConnectionsPage()
    {
        InitializeComponent();
        ViewModel.ConfirmDelete = ConfirmAsync;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConnectionsViewModel.Selected))
            {
                ShowPassword();
            }
        };
        Loaded += (_, _) => ShowPassword();
    }

    /// <summary>PasswordBox không x:Bind 2 chiều tiện như TextBox → đổi hồ sơ thì đặt lại tay.</summary>
    private void ShowPassword()
    {
        _settingPassword = true;
        PasswordBox.Password = ViewModel.Selected?.Password ?? string.Empty;
        _settingPassword = false;
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_settingPassword && ViewModel.Selected is { } profile)
        {
            profile.Password = PasswordBox.Password;
        }
    }

    private async Task<bool> ConfirmAsync(string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Xoá hồ sơ",
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "Xoá",
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
