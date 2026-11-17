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

/// <summary>Thước đo trong Editor (hướng B): thước kẻ mép khung ảnh, Chèn số đo vào ảnh, vẽ khi canvas có zoom.</summary>
public class EditorRulerTests
{
    [Theory]
    [InlineData(1.0, 50, 50)]    // 100%: vạch có số mỗi 50 px
    [InlineData(1.5, 75, 50)]    // màn hình 150% (khoảng cách tính theo pixel vật lý) → 50
    [InlineData(4.0, 50, 20)]    // phóng to 400% → 20 px ảnh = 80 px màn hình
    [InlineData(0.25, 50, 200)]  // thu nhỏ 25% → 200
    [InlineData(0.1, 50, 500)]
    [InlineData(8.0, 50, 10)]
    public void Buoc_vach_co_so_la_so_dep(double zoom, double minGap, int expected)
    {
        Assert.Equal(expected, Ruler.TickStep(zoom, minGap));
    }

    [Fact]
    public void Vach_nho_chia_10_5_2_hoac_khong_co()
    {
        Assert.Equal(10, Ruler.MinorTickStep(100, 1.0, 5));  // 10 px màn hình / vạch
        Assert.Equal(10, Ruler.MinorTickStep(50, 0.8, 5));   // 50 / 10 = 4 px màn hình: chật → chia 5
        Assert.Equal(1, Ruler.MinorTickStep(10, 8.0, 5));
        Assert.Equal(0, Ruler.MinorTickStep(1, 50, 5));      // bước 1 px: không chia nhỏ được
    }

    [Fact]
    public void Chen_chu_nhat_thanh_hinh_chu_nhat_va_o_chu()
    {
        var shapes = Ruler.ToAnnotations(Ruler.Box(10, 20, 109, 59), SKRect.Create(400, 300));

        var rect = Assert.IsType<RectangleAnnotation>(shapes[0]);
        Assert.Equal(new SKRect(10, 20, 110, 60), rect.Bounds);
        Assert.Equal(Ruler.LineColor, rect.Color);
        var label = Assert.IsType<TextAnnotation>(shapes[1]);
        Assert.Equal("100 × 40 px", label.Text);
        Assert.True(label.Bounds.Top >= 60, "ô chữ nằm dưới chữ nhật");
        Assert.Equal(60f, label.Bounds.MidX, 1);
    }

    [Fact]
    public void Chen_do_canh_thanh_2_duong_mui_ten_2_dau()
    {
        var shapes = Ruler.ToAnnotations(new RulerMeasurement(RulerMode.Edges, 3, 0, 102, 9, 50, 4), SKRect.Create(200, 10));

        var across = Assert.IsType<LineArrowAnnotation>(shapes[0]);
        Assert.Equal((new SKPoint(3, 4.5f), new SKPoint(103, 4.5f)), (across.Start, across.End));
        Assert.Equal((ArrowHead.Arrow, ArrowHead.Arrow), (across.StartHead, across.EndHead));
        var down = Assert.IsType<LineArrowAnnotation>(shapes[1]);
        Assert.Equal((ArrowHead.None, ArrowHead.None), (down.StartHead, down.EndHead)); // 10 px: ngắn quá, bỏ mũi tên
        var label = Assert.IsType<TextAnnotation>(shapes[2]);
        Assert.Equal("100 × 10 px", label.Text);
        Assert.True(label.Bounds.Left >= 0 && label.Bounds.Right <= 200, "ô chữ không ra ngoài ảnh");
    }

    [Fact]
    public void O_chu_sat_mep_duoi_thi_len_tren()
    {
        var shapes = Ruler.ToAnnotations(new RulerMeasurement(RulerMode.Line, 10, 290, 190, 290), SKRect.Create(200, 300));

        var label = Assert.IsType<TextAnnotation>(shapes[^1]);
        Assert.True(label.Bounds.Bottom <= 290, $"ô chữ phải lên trên đường đo: {label.Bounds}");
        Assert.True(label.Bounds.Right <= 200);
    }

    [Fact]
    public void Ve_khi_canvas_co_zoom_nhan_nam_trong_khung_anh()
    {
        // Ảnh 50×40 phóng 4 lần, dời 16 px như Editor: nhãn phải nằm trong khung ảnh (không theo cả canvas).
        using var bitmap = new SKBitmap(16 + 200 + 100, 16 + 160 + 100);
        bitmap.Erase(SKColors.White);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Translate(16, 16);
            canvas.Scale(4);
            Ruler.Draw(canvas, [Ruler.Box(10, 30, 39, 39)], 1f / 4, SKRect.Create(50, 40));
        }

        // Vạch chữ nhật nằm giữa pixel mép (x = 10,5 pixel ảnh = 58 pixel màn hình), mảnh 1 pixel màn hình.
        Assert.Contains(Ruler.LineColor, new[] { bitmap.GetPixel(57, 16 + 35 * 4), bitmap.GetPixel(58, 16 + 35 * 4) });
        bool darkOutside = false;
        for (int y = 16 + 160 + 2; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                darkOutside |= bitmap.GetPixel(x, y).Red < 100;
            }
        }
        Assert.False(darkOutside, "nhãn tràn xuống dưới khung ảnh");
    }
}

/// <summary>Dò cạnh chỉ ngang / chỉ dọc (2026-11-17).</summary>
public class RulerAxisTests
{
    private static readonly RulerMeasurement Gap = new(RulerMode.Edges, 3, 0, 102, 49, 50, 20); // 100 × 50

    [Fact]
    public void Nhan_va_copy_chi_co_chieu_dang_do()
    {
        Assert.Equal("100 × 50 px", Gap.Label);
        Assert.Equal("100 px", (Gap with { Axis = RulerAxis.Horizontal }).Label);
        Assert.Equal("50 px", (Gap with { Axis = RulerAxis.Vertical }).Label);
        var scaled = Gap with { Axis = RulerAxis.Horizontal, Scale = 1.5 };
        Assert.Equal("100 px (logic 66,7 (150%))", scaled.CopyText);
    }

    [Fact]
    public void Chieu_chi_ap_cho_do_canh()
    {
        var box = Ruler.Box(0, 0, 9, 4) with { Axis = RulerAxis.Horizontal };

        Assert.Equal("10 × 5 px", box.Label);
    }

    [Fact]
    public void Doi_vong_ca_2_ngang_doc()
    {
        Assert.Equal(RulerAxis.Horizontal, Ruler.NextAxis(RulerAxis.Both));
        Assert.Equal(RulerAxis.Vertical, Ruler.NextAxis(RulerAxis.Horizontal));
        Assert.Equal(RulerAxis.Both, Ruler.NextAxis(RulerAxis.Vertical));
        Assert.Equal(RulerAxis.Both, new AppSettings().RulerAxis);
    }

    [Fact]
    public void Chi_ngang_chi_ve_vach_ngang()
    {
        using var bitmap = new SKBitmap(200, 120);
        bitmap.Erase(SKColors.White);
        using (var canvas = new SKCanvas(bitmap))
        {
            Ruler.Draw(canvas, [new RulerMeasurement(RulerMode.Edges, 0, 0, 199, 119, 150, 5, Axis: RulerAxis.Horizontal)]);
        }

        Assert.Equal(Ruler.LineColor, bitmap.GetPixel(60, 5));      // vạch ngang qua hàng con trỏ
        Assert.NotEqual(Ruler.LineColor, bitmap.GetPixel(150, 80)); // không có vạch dọc qua cột con trỏ
    }

    [Fact]
    public void Chen_chi_doc_thanh_1_duong_va_o_chu()
    {
        var shapes = Ruler.ToAnnotations(Gap with { Axis = RulerAxis.Vertical }, SKRect.Create(200, 100));

        var line = Assert.IsType<LineArrowAnnotation>(shapes[0]);
        Assert.Equal((new SKPoint(50.5f, 0), new SKPoint(50.5f, 50)), (line.Start, line.End));
        Assert.Equal("50 px", Assert.IsType<TextAnnotation>(shapes[1]).Text);
        Assert.Equal(2, shapes.Count);
    }
}
