using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MeRon.Views;

/// <summary>
/// Hộp thoại dùng chung cho các trang: form thêm / sửa (Lưu - Xoá - Huỷ, lỗi hiện ngay trong hộp thoại và không đóng để
/// sửa tiếp - giống điện thoại "báo lỗi và không cho lưu"), xác nhận, thông báo.
/// </summary>
internal static class Dialogs
{
    /// <param name="save">Lưu; trả về thông báo lỗi (hộp thoại giữ nguyên) hoặc null (đóng).</param>
    /// <param name="delete">null = không có nút Xoá. Trả về lỗi hoặc null; hỏi xác nhận trước khi gọi.</param>
    /// <returns>true nếu đã lưu hoặc đã xoá (trang cần tải lại).</returns>
    public static async Task<bool> ShowFormAsync(XamlRoot root, string title, IEnumerable<UIElement> fields,
        Func<string?> save, Func<string?>? delete = null, string? deleteQuestion = null)
    {
        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false, IsOpen = false };
        var panel = new StackPanel { Spacing = 10, MinWidth = 420 };
        foreach (var field in fields)
        {
            panel.Children.Add(field);
        }
        panel.Children.Add(error);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = "Lưu",
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (delete is not null)
        {
            dialog.SecondaryButtonText = "Xoá";
        }

        var deleteRequested = false;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var message = save();
            if (message is not null)
            {
                error.Message = message;
                error.IsOpen = true;
                args.Cancel = true;
            }
        };
        dialog.SecondaryButtonClick += (_, _) => deleteRequested = true;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            return true;
        }
        if (!deleteRequested || delete is null)
        {
            return false;
        }

        // ContentDialog chỉ mở được 1 cái mỗi lúc → hỏi xác nhận sau khi form đã đóng.
        if (!await ConfirmAsync(root, "Xoá?", deleteQuestion ?? "Xoá mục này? Không hoàn tác được.", "Xoá"))
        {
            return false;
        }
        var deleteError = delete();
        if (deleteError is not null)
        {
            await ShowMessageAsync(root, "Không xoá được", deleteError);
            return false;
        }
        return true;
    }

    public static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message, string confirmText)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = SharedUI.Helpers.DialogContent.Scrollable(message, 480),
            PrimaryButtonText = confirmText,
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task ShowMessageAsync(XamlRoot root, string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = SharedUI.Helpers.DialogContent.Scrollable(message, 480),
            CloseButtonText = "Đóng",
        };
        await dialog.ShowAsync();
    }

    // ---- ô nhập cho form ----

    public static TextBox Text(string header, string? value, bool multiline = false, bool readOnly = false)
    {
        var box = new TextBox
        {
            Header = header,
            Text = value ?? string.Empty,
            IsReadOnly = readOnly,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 64 : 0,
        };
        AutomationProperties.SetName(box, header);
        return box;
    }

    /// <summary>Ô số tiền (VNĐ, số nguyên không âm, nhóm hàng nghìn).</summary>
    public static NumberBox Money(string header, long? value)
    {
        var box = new NumberBox
        {
            Header = header,
            Value = value ?? double.NaN,
            Minimum = 0,
            SmallChange = 1000,
            LargeChange = 10000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            NumberFormatter = Format.MoneyFormatter(),
        };
        AutomationProperties.SetName(box, header);
        return box;
    }

    public static long? ValueOf(NumberBox box) => double.IsNaN(box.Value) ? null : (long)Math.Round(box.Value);
}
