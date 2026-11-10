namespace CsvEditor.Services;

/// <summary>Kéo chọn vùng / kéo điền ra ngoài mép bảng: mỗi nhịp cuộn bao nhiêu dòng (cột). Càng xa mép càng nhanh, có trần để
/// không vọt quá xa khi chuột ra hẳn ngoài cửa sổ.</summary>
public static class EdgeScroll
{
    /// <summary>Mỗi chừng này px ra ngoài mép thì cuộn thêm 1 dòng mỗi nhịp.</summary>
    public const double PixelsPerStep = 24;

    public const int MaxStep = 20;

    /// <summary>Khoảng cách từ con trỏ ra mép (âm = phía trên / trái, dương = phía dưới / phải, 0 = trong vùng ô). Ngoài vùng
    /// [<paramref name="low"/>, <paramref name="high"/>] mới khác 0.</summary>
    public static double Overshoot(double position, double low, double high) =>
        position < low ? position - low : position > high ? position - high : 0;

    /// <summary>Số dòng (cột) cuộn mỗi nhịp, có dấu theo hướng; 0 khi con trỏ còn trong vùng ô.</summary>
    public static int Step(double overshoot)
    {
        if (overshoot == 0)
        {
            return 0;
        }

        var step = Math.Min(1 + (int)(Math.Abs(overshoot) / PixelsPerStep), MaxStep);
        return overshoot < 0 ? -step : step;
    }

    /// <summary>Cột rộng hơn dòng nhiều (100–800 px) nên cuộn ngang chậm hơn: số cột mỗi nhịp có phần lẻ, cộng dồn qua
    /// <see cref="TakeWhole"/> - sát mép 1 cột / 4 nhịp, xa dần nhanh lên, tối đa <see cref="MaxColumnRate"/>.</summary>
    public static double ColumnRate(double overshoot)
    {
        if (overshoot == 0)
        {
            return 0;
        }

        var rate = Math.Min(0.25 + (Math.Abs(overshoot) / 100), MaxColumnRate);
        return overshoot < 0 ? -rate : rate;
    }

    public const double MaxColumnRate = 3;

    /// <summary>Cộng <paramref name="rate"/> vào phần dồn, lấy ra phần nguyên (có dấu), phần lẻ giữ lại. Đổi hướng thì bỏ phần dồn cũ.</summary>
    public static int TakeWhole(ref double accumulated, double rate)
    {
        if (rate == 0 || Math.Sign(rate) != Math.Sign(accumulated))
        {
            accumulated = 0;
        }

        accumulated += rate;
        var whole = (int)Math.Truncate(accumulated);
        accumulated -= whole;
        return whole;
    }
}
