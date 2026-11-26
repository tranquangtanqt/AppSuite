using DbTools.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace DbTools.Views;

/// <summary>Hàm nhỏ dùng trong x:Bind.</summary>
internal static class Ui
{
    private static readonly Brush Green = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x16, 0xA3, 0x4A));
    private static readonly Brush Blue = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x25, 0x63, 0xEB));
    private static readonly Brush Orange = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xD9, 0x77, 0x06));
    private static readonly Brush Red = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xDC, 0x26, 0x26));
    private static readonly Brush Gray = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x80, 0x80, 0x80));

    public static Brush StatusBrush(RowStatus status) => status switch
    {
        RowStatus.Ok => Green,
        RowStatus.Info or RowStatus.Running => Blue,
        RowStatus.Warning => Orange,
        RowStatus.Error => Red,
        _ => Gray,
    };

    public static Brush ResultBrush(bool success) => success ? Green : Red;

    public static Brush FailBrush(bool failed) => failed ? Red : Green;

    /// <summary>Vạch / nhãn hồ sơ Production (đỏ); hồ sơ thường trong suốt.</summary>
    public static Brush ProductionBrush(bool production) => production ? Red : Transparent;

    public static Brush FolderBrush => Orange;

    private static readonly Brush Transparent = new SolidColorBrush(Colors.Transparent);

    public static string ResultGlyph(bool success) => success ? "" : "";

    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility HiddenIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Phần nội dung của tab <paramref name="index"/> (SelectorBar) chỉ hiện khi tab đó đang chọn.</summary>
    public static Visibility TabVisible(int current, int index) => current == index ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>ComboBox gõ được (IsEditable) nhận Text qua binding trước khi load xong thì ô hiện trống dù Text đã có giá trị →
    /// gắn vào Loaded: đặt lại chữ khi control đã sẵn sàng.</summary>
    public static void RefreshEditableText(object sender, RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.ComboBox { IsEditable: true } box && !string.IsNullOrEmpty(box.Text))
        {
            var text = box.Text;
            box.DispatcherQueue.TryEnqueue(() =>
            {
                box.Text = string.Empty;
                box.Text = text;
            });
        }
    }

    /// <summary>Nút ✕ của ComboBox gõ được. Chữ đặt từ code mà không có mục tương ứng (vd DB của hồ sơ, danh sách chưa nạp) được
    /// vẽ bằng 1 TextBlock riêng trong phần "ContentPresenter" của khuôn; đặt Text rỗng thì ô gõ được xoá nhưng TextBlock đó vẫn giữ
    /// chữ cũ → ô vẫn hiện chữ dù giá trị đã rỗng. Bỏ mục đang chọn, đặt Text rỗng rồi xoá luôn TextBlock đó (không đụng
    /// TextBlock chữ gợi ý PlaceholderTextBlock mà ComboBox tự đặt vào chỗ đó).</summary>
    public static void ClearEditable(Microsoft.UI.Xaml.Controls.ComboBox box)
    {
        box.SelectedItem = null;
        box.Text = string.Empty;
        if (VisualTreeHelper.GetChildrenCount(box) > 0 && VisualTreeHelper.GetChild(box, 0) is Microsoft.UI.Xaml.Controls.Panel root
            && root.Children.OfType<Microsoft.UI.Xaml.Controls.ContentPresenter>().FirstOrDefault(c => c.Name == "ContentPresenter")
                is { Content: Microsoft.UI.Xaml.Controls.TextBlock shown } && shown.Name != "PlaceholderTextBlock") // chữ gợi ý: giữ
        {
            shown.Text = string.Empty;
        }
    }

    /// <summary>ComboBox gõ được nhớ riêng mục đã chốt (gõ / chọn): mở danh sách rồi đóng mà không chọn gì (Esc, bấm ra ngoài) thì
    /// nó trả về mục đó, nên chữ đặt từ code (vd vừa đổi server) bị mất hoặc thành DB của server cũ → trả lại chữ lúc mở.</summary>
    public static void KeepTextIfNothingPicked(params Microsoft.UI.Xaml.Controls.ComboBox[] boxes)
    {
        foreach (var box in boxes)
        {
            var text = string.Empty;
            object? item = null;
            var picked = false;
            var escape = false;
            box.DropDownOpened += (_, _) =>
            {
                (text, item, picked, escape) = (box.Text, box.SelectedItem, false, false);
                if (item is not null && !Equals(item, text))
                {
                    // Mục đang nhớ lệch với chữ: bỏ, không thì chọn đúng mục đó không được tính là đổi lựa chọn.
                    item = box.SelectedItem = null;
                    box.Text = text;
                }
            };
            // Chọn thật: lựa chọn đổi khi danh sách còn mở (chuột) hoặc chốt bằng Enter / Tab; trả về mục cũ: lúc Esc hoặc sau khi
            // danh sách đã đóng.
            box.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler((_, e) =>
            {
                escape |= e.Key == VirtualKey.Escape;
                picked |= e.Key is VirtualKey.Enter or VirtualKey.Tab;
            }), handledEventsToo: true);
            box.SelectionChanged += (_, e) => picked |= box.IsDropDownOpen && !escape && e.AddedItems.Count > 0;
            box.DropDownClosed += (_, _) => box.DispatcherQueue.TryEnqueue(() =>
            {
                if (!picked && box.Text != text)
                {
                    box.SelectedItem = item;
                    box.Text = text;
                }
            });
        }
    }

    public static Visibility VisibleIfText(string? value) => VisibleIf(!string.IsNullOrEmpty(value));

    /// <summary>Màu chữ cột Loại ở Nhật ký server: lỗi đỏ, DDL / xoá cam, sửa / thêm xanh dương, còn lại xám.</summary>
    public static Brush LogKindBrush(Core.LogKind kind) => kind switch
    {
        Core.LogKind.Error => Red,
        Core.LogKind.Ddl or Core.LogKind.Delete => Orange,
        Core.LogKind.Insert or Core.LogKind.Update => Blue,
        Core.LogKind.Select => Green,
        _ => Gray,
    };

    public static Brush WarnBrush(bool warning) => warning ? Orange : Gray;

    public static string ConfigBullet(bool warning) => warning ? "⚠" : "•";

    public static bool And2(bool a, bool b) => a && b;

    public static bool And3(bool a, bool b, bool c) => a && b && c;
}
