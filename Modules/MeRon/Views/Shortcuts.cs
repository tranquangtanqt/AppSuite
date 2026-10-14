using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace MeRon.Views;

/// <summary>
/// Phím tắt của 1 trang (danh sách đầy đủ: mục "Phím tắt" trong F1). Gắn lên chính trang: chỉ chạy khi trang đang hiện (trang
/// cache đang ẩn không còn trong cây giao diện). Không hiện tooltip phím tắt (WinUI tự hiện khi rê chuột vùng trống), và bỏ qua
/// khi đang mở hộp thoại (Thêm / sửa khách... ; mở thêm hộp thoại thứ 2 sẽ lỗi).
/// </summary>
internal static class Shortcuts
{
    public static void Add(UIElement page, VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        page.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, args) =>
        {
            if (page.XamlRoot is { } root && VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(p => p.Child is ContentDialog))
            {
                return;
            }
            args.Handled = true;
            action();
        };
        page.KeyboardAccelerators.Add(accelerator);
    }

    public static void Ctrl(UIElement page, VirtualKey key, Action action) => Add(page, key, VirtualKeyModifiers.Control, action);

    /// <summary>Ctrl+F: con trỏ vào ô tìm / lọc, chọn sẵn chữ cũ để gõ đè.</summary>
    public static void Search(UIElement page, Control box) => Ctrl(page, VirtualKey.F, () =>
    {
        box.Focus(FocusState.Keyboard);
        if (box is TextBox text)
        {
            text.SelectAll();
        }
    });

    /// <summary>Ctrl+1…4: chọn tab trạng thái (Tất cả / Đã lên đơn / Đã thanh toán / Huỷ) - như bấm vào tab.</summary>
    public static void StatusTabs(UIElement page, SelectorBar bar)
    {
        VirtualKey[] keys = [VirtualKey.Number1, VirtualKey.Number2, VirtualKey.Number3, VirtualKey.Number4];
        for (var i = 0; i < keys.Length; i++)
        {
            var index = i;
            Ctrl(page, keys[i], () =>
            {
                if (index < bar.Items.Count)
                {
                    bar.SelectedItem = bar.Items[index];
                }
            });
        }
    }

    /// <summary>
    /// Esc trên form: quay lại (không lưu). Đang mở danh sách gợi ý / lịch / menu (popup) thì để Esc đóng popup đó trước.
    /// 2 đường: PreviewKeyDown của trang khi con trỏ trong trang (ô gợi ý AutoSuggestBox tự nuốt Esc kể cả khi không mở gợi ý,
    /// phím tắt thường không tới), phím tắt khi con trỏ chưa ở trong trang (vừa mở form bằng Ctrl+Shift+N / Sao chép đơn).
    /// </summary>
    public static void EscapeBack(UIElement page, Action back)
    {
        bool TryBack()
        {
            if (page.XamlRoot is not { } root || VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(p => p.Child is not ToolTip))
            {
                return false;
            }
            back();
            return true;
        }

        page.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape && TryBack())
            {
                e.Handled = true;
            }
        };
        page.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        var accelerator = new KeyboardAccelerator { Key = VirtualKey.Escape };
        accelerator.Invoked += (_, args) => args.Handled = TryBack();
        page.KeyboardAccelerators.Add(accelerator);
    }
}
