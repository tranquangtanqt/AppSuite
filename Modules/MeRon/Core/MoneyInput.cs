namespace MeRon.Core;

/// <summary>
/// Gõ tiền theo nghìn: số nguyên 2–999 hiểu là nghìn đồng (25 → 25.000) - ô Đơn giá món tự do, Giá bán món, Giảm giá và cột giá /
/// giảm trong chữ dán Nhập đơn hàng loạt. Giữ nguyên 0 và 1 (giá ≤ 1đ = đơn cần xác nhận), số lẻ (1,5) và số từ 1.000.
/// </summary>
public static class MoneyInput
{
    public static double Thousands(double value) =>
        value >= 2 && value < 1000 && value == Math.Floor(value) ? value * 1000 : value;

    public static long Thousands(long value) => value is >= 2 and < 1000 ? value * 1000 : value;
}
