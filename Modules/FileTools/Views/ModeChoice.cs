using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

/// <summary>Nhóm RadioButton rời (mỗi lựa chọn cùng hàng với ô số của nó) ↔ 1 chỉ số ModeIndex trong ViewModel.
/// XAML: IsChecked / IsEnabled = {x:Bind views:ModeChoice.Is(ViewModel.ModeIndex, 1), Mode=OneWay}; Checked → <see cref="IndexOf"/>.</summary>
internal static class ModeChoice
{
    public static bool Is(int modeIndex, int value) => modeIndex == value;

    /// <summary>Tag của RadioButton vừa chọn ("0", "1", "2").</summary>
    public static int IndexOf(object sender) => int.Parse((string)((FrameworkElement)sender).Tag);
}
