using System.Globalization;

namespace MeRon.Core;

/// <summary>Số / tiền luôn theo kiểu Việt Nam (2.617; 25.000 đ) - không theo ngôn ngữ Windows của máy đang chạy.</summary>
public static class VnFormat
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("vi-VN");

    public static string Number(long value) => value.ToString("N0", Culture);

    /// <summary>25000 → "25.000 đ"; null → "".</summary>
    public static string Money(long? value) => value is null ? string.Empty : Number(value.Value) + " đ";
}
