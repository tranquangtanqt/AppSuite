using MeRon.Core;
using Windows.Globalization.NumberFormatting;

namespace MeRon.Views;

/// <summary>Định dạng hiển thị dùng trong x:Bind.</summary>
internal static class Format
{
    public static string Money(long? value) => VnFormat.Money(value);

    public static string Number(long value) => VnFormat.Number(value);

    public static DecimalFormatter MoneyFormatter() => new(["vi-VN"], "VN")
    {
        IsGrouped = true,
        FractionDigits = 0,
        NumberRounder = new IncrementNumberRounder { Increment = 1, RoundingAlgorithm = RoundingAlgorithm.RoundHalfUp },
    };

    /// <summary>Như <see cref="MoneyFormatter"/>, gõ 2–999 hiểu là nghìn đồng (Đơn giá món tự do, Giá bán, Giảm giá).</summary>
    public static ThousandsFormatter PriceFormatter(Microsoft.UI.Xaml.Controls.NumberBox box) => new(box, MoneyFormatter());
}
