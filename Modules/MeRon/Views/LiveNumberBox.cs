using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Globalization.NumberFormatting;

namespace MeRon.Views;

/// <summary>
/// NumberBox chỉ ghi Value khi rời ô / Enter - nên lúc đang gõ (số lượng, giảm giá, đơn giá) Tổng chưa đổi. Gắn vào ô chữ bên
/// trong: mỗi lần gõ thì báo số đang gõ (<c>preview</c>, null = chưa phải số) để tính tạm, rời ô thì báo null (về giá trị đã ghi).
/// Không ghi Value lúc đang gõ: NumberBox sẽ định dạng lại chữ và làm nhảy con trỏ. Kèm canh lề số (tiền canh phải).
/// </summary>
internal static class LiveNumberBox
{
    /// <summary>Gắn trong Loaded của ô (template đã có); gắn 1 lần / ô.</summary>
    public static void Attach(NumberBox box, Action<double?> preview, TextAlignment alignment)
    {
        if (box.Tag is "live" || FindTextBox(box) is not { } input)
        {
            return;
        }
        box.Tag = "live";
        input.TextAlignment = alignment;
        input.TextChanged += (_, _) => preview(Parse(box, input.Text));
        input.LostFocus += (_, _) => preview(null);
    }

    private static double? Parse(NumberBox box, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        if (box.NumberFormatter is INumberParser parser && parser.ParseDouble(text.Trim()) is { } parsed)
        {
            return parsed;
        }
        return double.TryParse(text, NumberStyles.Number, CultureInfo.GetCultureInfo("vi-VN"), out var value) ? value : null;
    }

    private static TextBox? FindTextBox(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBox textBox)
            {
                return textBox;
            }
            if (FindTextBox(child) is { } found)
            {
                return found;
            }
        }
        return null;
    }
}
