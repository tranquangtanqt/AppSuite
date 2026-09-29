using System.Globalization;

namespace CsvEditor.Services;

/// <summary>
/// Đọc 1 ô CSV là số cho Sort / Filter / Statistics, không phụ thuộc định dạng số của máy.
///
/// Trước đây dùng <c>double.TryParse(text)</c> theo culture máy: máy đặt kiểu Việt / Đức (dấu phẩy
/// thập phân, dấu chấm phân nhóm) hiểu "2.5" thành 25 → sắp xếp, lọc, tổng đều sai.
///
/// Quy tắc bây giờ:
/// 1. Dấu chấm thập phân (InvariantCulture) - cách hầu hết file CSV ghi số, luôn được hiểu.
/// 2. Không khớp thì thử dấu thập phân của máy (vd máy vi-VN hiểu "2,5" = 2,5 - file CSV dùng ';' kiểu
///    châu Âu hay ghi số như vậy).
/// Cả 2 bước đều KHÔNG nhận dấu phân nhóm hàng nghìn ("1,000" / "1.000" có thể là 1000 hoặc 1 tuỳ
/// nơi - không đoán): ô như vậy so sánh như chữ.
/// </summary>
public static class CsvNumber
{
    // Float = khoảng trắng đầu/cuối, dấu âm, dấu thập phân, số mũ (1e3). Không có AllowThousands.
    private const NumberStyles Style = NumberStyles.Float;

    public static bool TryParse(string text, out double value)
    {
        if (double.TryParse(text, Style, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        var current = CultureInfo.CurrentCulture;
        return current.NumberFormat.NumberDecimalSeparator != "."
            && double.TryParse(text, Style, current, out value);
    }
}
