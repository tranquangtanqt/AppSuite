using FileTools.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

public sealed partial class DuplicatesPage : Page
{
    public DuplicatesViewModel ViewModel { get; } = new();

    public DuplicatesPage()
    {
        InitializeComponent();
        ScrollFit.Attach(Scroller, Root, 760);
        DropHelper.Attach(Root, paths =>
            ViewModel.Folders = string.Join("; ", paths.Select(p => Directory.Exists(p) ? p : Path.GetDirectoryName(p)!).Distinct()));
        ViewModel.Confirm = async message =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Chuyển vào Thùng rác",
                Content = message,
                PrimaryButtonText = "Chuyển",
                CloseButtonText = "Huỷ",
                DefaultButton = ContentDialogButton.Close,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        };
    }
}
