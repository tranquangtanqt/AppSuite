using MeRon.Core;
using Microsoft.UI.Xaml.Controls;
using Windows.Globalization.NumberFormatting;

namespace MeRon.Views;

/// <summary>
/// Định dạng / đọc số cho ô tiền gõ theo nghìn (<see cref="MoneyInput"/>): NumberBox đọc chữ người dùng gõ qua bộ đọc này nên
/// 25 → 25.000 lúc rời ô / Enter (và cả Tổng tạm tính lúc đang gõ - LiveNumberBox). Giá trị gán từ code (mở đơn cũ có giá 500đ)
/// không đi qua đây; NumberBox đọc lại chữ của chính giá trị đang có (rời ô mà không sửa) thì giữ nguyên, không nhân lần nữa.
/// </summary>
public sealed partial class ThousandsFormatter(NumberBox box, DecimalFormatter inner) : INumberFormatter2, INumberParser
{
    public string FormatDouble(double value) => inner.FormatDouble(value);

    public string FormatInt(long value) => inner.FormatInt(value);

    public string FormatUInt(ulong value) => inner.FormatUInt(value);

    public double? ParseDouble(string text)
    {
        if (inner.ParseDouble(text) is not { } value)
        {
            return null;
        }
        return IsCurrentText(text) ? value : MoneyInput.Thousands(value);
    }

    public long? ParseInt(string text) => inner.ParseInt(text) is { } value ? (IsCurrentText(text) ? value : MoneyInput.Thousands(value)) : null;

    public ulong? ParseUInt(string text) => inner.ParseUInt(text) is { } value ? (IsCurrentText(text) ? value : (ulong)MoneyInput.Thousands((long)value)) : null;

    private bool IsCurrentText(string text) => !double.IsNaN(box.Value) && text.Trim() == inner.FormatDouble(box.Value);
}
