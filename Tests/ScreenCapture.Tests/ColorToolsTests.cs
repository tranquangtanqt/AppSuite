using ScreenCapture.Models;
using SkiaSharp;

namespace ScreenCapture.Tests;

public class ColorToolsTests
{
    private static readonly SKColor Sample = new(117, 115, 105); // màu trong ảnh Bảng màu của PicPick

    [Fact]
    public void Formats_html_and_rgb()
    {
        Assert.Equal("#757369", ColorTools.ToHtml(Sample));
        Assert.Equal("rgb(117, 115, 105)", ColorTools.ToRgb(Sample));
        Assert.Equal("#757369", ColorTools.Format(Sample, ColorCopyFormat.Html));
        Assert.Equal("rgb(117, 115, 105)", ColorTools.Format(Sample, ColorCopyFormat.Rgb));
    }

    [Theory]
    [InlineData("#757369")]
    [InlineData("757369")]
    [InlineData("  #757369 ")]
    [InlineData("#757369 ")]
    public void Parses_html(string text)
    {
        Assert.True(ColorTools.TryParseHtml(text, out var color));
        Assert.Equal(Sample, color);
    }

    [Fact]
    public void Parses_lowercase_html() =>
        Assert.True(ColorTools.TryParseHtml("#ed1c24", out var c) && c == new SKColor(0xED, 0x1C, 0x24));

    [Fact]
    public void Parses_short_html() =>
        Assert.True(ColorTools.TryParseHtml("#F80", out var c) && c == new SKColor(0xFF, 0x88, 0x00));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("#75736")]
    [InlineData("#GGGGGG")]
    [InlineData("rgb(1,2,3)")]
    public void Rejects_bad_html(string? text) => Assert.False(ColorTools.TryParseHtml(text, out _));

    [Fact]
    public void Hsl_matches_picpick_scale() => Assert.Equal((35, 14, 111), ColorTools.ToHsl(Sample));

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(255, 255, 255)]
    [InlineData(255, 0, 0)]
    [InlineData(0, 162, 232)]
    [InlineData(117, 115, 105)]
    [InlineData(136, 0, 21)]
    public void Hsl_round_trip_stays_close(byte r, byte g, byte b)
    {
        var (h, s, l) = ColorTools.ToHsl(new SKColor(r, g, b));
        var back = ColorTools.FromHsl(h, s, l);
        // HSL 0–255 làm tròn → lệch tối đa vài đơn vị.
        Assert.InRange(Math.Abs(back.Red - r), 0, 3);
        Assert.InRange(Math.Abs(back.Green - g), 0, 3);
        Assert.InRange(Math.Abs(back.Blue - b), 0, 3);
    }

    [Fact]
    public void Shades_of_black_get_lighter_and_white_darker()
    {
        Assert.Equal(new SKColor(128, 128, 128), ColorTools.Shades(SKColors.Black)[0]);
        Assert.Equal(new SKColor(13, 13, 13), ColorTools.Shades(SKColors.Black)[4]);
        Assert.Equal(new SKColor(242, 242, 242), ColorTools.Shades(SKColors.White)[0]);
        Assert.Equal(new SKColor(128, 128, 128), ColorTools.Shades(SKColors.White)[4]);
    }

    [Fact]
    public void Shades_of_a_color_go_from_light_to_dark_with_same_hue()
    {
        var red = new SKColor(0xED, 0x1C, 0x24);
        var shades = ColorTools.Shades(red);
        Assert.Equal(5, shades.Count);
        var lums = shades.Select(c => ColorTools.ToHslDouble(c).L).ToList();
        Assert.Equal(lums.OrderByDescending(x => x), lums);
        Assert.True(lums[0] > ColorTools.ToHslDouble(red).L && lums[4] < ColorTools.ToHslDouble(red).L);
        double hue = ColorTools.ToHslDouble(red).H;
        Assert.All(shades, c =>
        {
            double d = Math.Abs(ColorTools.ToHslDouble(c).H - hue);
            Assert.True(Math.Min(d, 360 - d) < 3, $"lệch màu {d}");
        });
    }

    [Fact]
    public void Custom_colors_newest_first_no_duplicates_max_20()
    {
        var list = new List<string>();
        for (int i = 0; i < 25; i++)
        {
            list = ColorTools.AddCustomColor(list, new SKColor((byte)i, 0, 0));
        }
        Assert.Equal(ColorTools.MaxCustomColors, list.Count);
        Assert.Equal("#180000", list[0]);   // màu mới nhất ở đầu
        Assert.Equal("#050000", list[^1]);  // 5 màu cũ nhất bị đẩy ra

        list = ColorTools.AddCustomColor(list, new SKColor(10, 0, 0)); // trùng → lên đầu, không thêm bản sao
        Assert.Equal("#0A0000", list[0]);
        Assert.Single(list, c => c == "#0A0000");
        Assert.Equal(ColorTools.MaxCustomColors, list.Count);
    }

    [Fact]
    public void Custom_colors_skip_bad_entries_from_settings_file() =>
        Assert.Equal(["#757369", "#FF0000"], ColorTools.AddCustomColor(["xyz", "ff0000", "#757369"], Sample));
}
