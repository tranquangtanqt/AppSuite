using ScreenCapture.Models;
using SkiaSharp;

namespace ScreenCapture.Tests;

public class RulerTests
{
    private const uint White = 0xFFFFFFFF, Black = 0xFF000000, Gray = 0xFFF0F0F0;

    /// <summary>Ảnh 20×10 nền trắng, 2 ô đen: cột 0–2 và cột 15–19 (mọi hàng) → khoảng trống giữa = cột 3..14.</summary>
    private static uint[] TwoBlocks(out int width, out int height)
    {
        width = 20;
        height = 10;
        var pixels = new uint[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                pixels[y * width + x] = x <= 2 || x >= 15 ? Black : White;
            }
        }
        return pixels;
    }

    [Fact]
    public void Do_canh_dung_o_cho_mau_doi()
    {
        var pixels = TwoBlocks(out int w, out int h);

        var m = Ruler.FindEdges(pixels, w, h, 8, 4, tolerance: 0);

        Assert.Equal((3, 14), (m.X1, m.X2));
        Assert.Equal(12, m.Width);
        Assert.Equal((0, 9), (m.Y1, m.Y2)); // không có gì chặn theo chiều dọc → tới mép ảnh
        Assert.Equal(10, m.Height);
        Assert.Equal((8, 4), (m.CursorX, m.CursorY));
        Assert.Equal("12 × 10 px", m.Label);
    }

    [Fact]
    public void Do_canh_tren_khoi_den_do_chinh_khoi_do()
    {
        var pixels = TwoBlocks(out int w, out int h);

        var m = Ruler.FindEdges(pixels, w, h, 16, 0, tolerance: 0);

        Assert.Equal((15, 19), (m.X1, m.X2));
        Assert.Equal(5, m.Width);
    }

    [Fact]
    public void Do_nhay_cho_qua_mau_gan_giong()
    {
        var pixels = TwoBlocks(out int w, out int h);
        pixels[4 * w + 10] = Gray; // 1 pixel xám nhạt giữa khoảng trắng (bóng mờ, khử răng cưa)

        var strict = Ruler.FindEdges(pixels, w, h, 6, 4, tolerance: 0);
        var loose = Ruler.FindEdges(pixels, w, h, 6, 4, tolerance: 30);

        Assert.Equal(9, strict.X2);
        Assert.Equal(14, loose.X2);
    }

    [Fact]
    public void Do_canh_ngoai_anh_thi_kep_vao_trong()
    {
        var pixels = TwoBlocks(out int w, out int h);

        var m = Ruler.FindEdges(pixels, w, h, 100, -5, tolerance: 0);

        Assert.Equal((19, 0), (m.CursorX, m.CursorY));
    }

    [Fact]
    public void Do_canh_tren_SKBitmap()
    {
        using var bitmap = new SKBitmap(30, 20);
        bitmap.Erase(SKColors.White);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = SKColors.Blue })
        {
            canvas.DrawRect(new SKRect(0, 0, 30, 5), paint);   // thanh trên: hàng 0..4
            canvas.DrawRect(new SKRect(0, 12, 30, 20), paint); // thanh dưới: hàng 12..19
        }

        var m = Ruler.FindEdges(bitmap, 10, 8, Ruler.DefaultTolerance);

        Assert.Equal((5, 11), (m.Y1, m.Y2));
        Assert.Equal(7, m.Height);
        Assert.Equal(30, m.Width);
    }

    [Fact]
    public void Chu_nhat_tinh_ca_2_dau_theo_thu_tu_bat_ky()
    {
        var m = Ruler.Box(110, 40, 10, 9);

        Assert.Equal((10, 9, 110, 40), (m.X1, m.Y1, m.X2, m.Y2));
        Assert.Equal("101 × 32 px", m.Label);
    }

    [Fact]
    public void Duong_thang_dx_dy_va_do_dai()
    {
        var m = new RulerMeasurement(RulerMode.Line, 0, 0, 30, 40);

        Assert.Equal(30, m.Width);
        Assert.Equal(40, m.Height);
        Assert.Equal(50, m.Length, 6);
        Assert.Equal("Δx 30 · Δy 40 · 50 px", m.Label);
    }

    [Fact]
    public void Do_dai_le_ghi_1_chu_so_thap_phan_dau_phay()
    {
        var m = new RulerMeasurement(RulerMode.Line, 0, 0, 100, 40);

        Assert.Equal("Δx 100 · Δy 40 · 107,7 px", m.Label);
    }

    [Theory]
    [InlineData(10, 2, 30, 7, 30, 2)]   // ngang dài hơn → giữ Y của A
    [InlineData(10, 2, 13, 50, 10, 50)] // dọc dài hơn → giữ X của A
    public void Shift_dinh_thanh_ngang_hoac_doc(int ax, int ay, int bx, int by, int ex, int ey)
    {
        Assert.Equal((ex, ey), Ruler.SnapLine(ax, ay, bx, by));
    }

    [Fact]
    public void Man_hinh_100_phan_tram_khong_ghi_pixel_logic()
    {
        var m = Ruler.Box(0, 0, 119, 31, scale: 1.0);

        Assert.Null(m.LogicalLabel);
        Assert.Equal("120 × 32 px", m.CopyText);
    }

    [Fact]
    public void Man_hinh_150_phan_tram_ghi_kem_pixel_logic()
    {
        var box = Ruler.Box(0, 0, 119, 31, scale: 1.5);
        var line = new RulerMeasurement(RulerMode.Line, 0, 0, 150, 0, Scale: 1.5);

        Assert.Equal("logic 80 × 21,3 (150%)", box.LogicalLabel);
        Assert.Equal("120 × 32 px (logic 80 × 21,3 (150%))", box.CopyText);
        Assert.Equal("logic 100 (150%)", line.LogicalLabel);
    }

    [Fact]
    public void Ve_so_do_len_anh()
    {
        using var bitmap = new SKBitmap(200, 120);
        bitmap.Erase(SKColors.White);
        using (var canvas = new SKCanvas(bitmap))
        {
            Ruler.Draw(canvas, [Ruler.Box(20, 20, 79, 49), new RulerMeasurement(RulerMode.Line, 100, 100, 180, 100),
                                new RulerMeasurement(RulerMode.Edges, 0, 0, 199, 10, 150, 5)]);
        }

        Assert.Equal(Ruler.LineColor, bitmap.GetPixel(20, 35));  // cạnh trái chữ nhật
        Assert.Equal(Ruler.LineColor, bitmap.GetPixel(140, 100)); // giữa đoạn thẳng
        Assert.Equal(Ruler.LineColor, bitmap.GetPixel(60, 5));    // vạch ngang của Dò cạnh qua hàng con trỏ
    }
}

public class RulerHotkeyTests
{
    [Fact]
    public void Thuoc_do_mac_dinh_Ctrl_Shift_M()
    {
        var binding = AppSettings.DefaultHotkeys().Single(h => h.Action == HotkeyAction.Ruler);

        Assert.Equal("Ctrl+Shift+M", binding.Describe());
    }
}
