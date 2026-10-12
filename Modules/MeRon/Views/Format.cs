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
}
