using DbTools.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

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

    public static bool And2(bool a, bool b) => a && b;

    public static bool And3(bool a, bool b, bool c) => a && b && c;
}
