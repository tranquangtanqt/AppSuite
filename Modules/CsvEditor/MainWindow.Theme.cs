using CsvEditor.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CsvEditor;

/// <summary>Nút Giao diện (Theo Windows / Sáng / Tối): lưu vào Data\Config\settings.json, áp dụng lúc app khởi động (App.xaml.cs)
/// nên đổi xong hỏi Mở lại ngay (hỏi lưu thay đổi trước, mở lại đúng file đang mở) / Để lần sau.</summary>
public sealed partial class MainWindow
{
    private readonly AppSettingsStore _settings = new();

    private void InitTheme()
    {
        // Đánh dấu lựa chọn đã lưu (có thể khác giao diện đang chạy nếu lần trước chọn Để lần sau).
        var saved = _settings.LoadTheme();
        ThemeSystemItem.IsChecked = saved == AppTheme.System;
        ThemeLightItem.IsChecked = saved == AppTheme.Light;
        ThemeDarkItem.IsChecked = saved == AppTheme.Dark;
    }

    private async void ThemeItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem { Tag: string tag, Text: string name } || !Enum.TryParse<AppTheme>(tag, out var theme))
        {
            return;
        }
        _settings.SaveTheme(theme);
        if (theme == App.Theme)
        {
            ViewModel.StatusMessage = $"Giao diện: {name}";
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Đổi giao diện",
            Content = $"Giao diện {name} áp dụng khi mở lại CsvEditor. Mở lại ngay?",
            PrimaryButtonText = "Mở lại ngay",
            CloseButtonText = "Để lần sau",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            ViewModel.StatusMessage = $"Giao diện {name} áp dụng từ lần mở CsvEditor sau.";
            return;
        }
        if (!await ConfirmSaveChangesAsync("mở lại"))
        {
            return;
        }
        AppLog.For("App").LogInformation("Mở lại để đổi giao diện: {Theme}", theme);
        _closeConfirmed = true;
        App.Restart(ViewModel.CurrentFilePath);
    }

    /// <summary>Lúc mở app: tham số dòng lệnh là đường dẫn file (Mở lại để đổi giao diện truyền file đang mở) thì mở file đó - trừ khi
    /// vừa mở bản khôi phục.</summary>
    private async Task OpenStartupFileAsync()
    {
        var path = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(File.Exists);
        if (path is not null && ViewModel.CurrentFilePath is null)
        {
            await OpenPathAsync(path);
        }
    }
}
