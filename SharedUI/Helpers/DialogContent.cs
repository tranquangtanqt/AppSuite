using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SharedUI.Helpers;

/// <summary>Nội dung cho ContentDialog tạo trong code. ContentDialog của WinUI tắt cuộn dọc của chính nó (cố định trong template,
/// không đổi bằng style được), nên nội dung dài hơn cửa sổ - thông báo lỗi dài, danh sách file - bị cắt mất phần dưới. Bọc
/// trong ScrollViewer (chỉ hiện thanh cuộn khi cần).</summary>
public static class DialogContent
{
    /// <summary>Chữ xuống dòng, chọn / copy được (thông báo lỗi), tự cuộn khi dài.</summary>
    public static ScrollViewer Scrollable(string text, double maxWidth = 520) =>
        Scrollable(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, MaxWidth = maxWidth });

    /// <summary>Phần tử bất kỳ (vd StackPanel nhiều ô), tự cuộn khi dài hơn cửa sổ.</summary>
    public static ScrollViewer Scrollable(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };
}
