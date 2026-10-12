using Microsoft.UI.Xaml;

namespace MeRon.Views;

/// <summary>Hàm nhỏ dùng trong x:Bind.</summary>
internal static class Ui
{
    public static bool HasText(string? value) => !string.IsNullOrEmpty(value);

    public static bool Not(bool value) => !value;

    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VisibleIfText(string? value) => VisibleIf(!string.IsNullOrEmpty(value));
}
