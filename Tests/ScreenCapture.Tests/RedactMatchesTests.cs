using System.Collections.ObjectModel;
using ScreenCapture.Commands;
using ScreenCapture.Models;
using SkiaSharp;

namespace ScreenCapture.Tests;

/// <summary>Tìm chữ → "Che Mosaic (N)" / "Làm mờ (N)": mỗi chỗ khớp thành 1 hình Che, cả nhóm 1 bước Undo
/// (EditorViewModel.AddRedactions dựng đúng như dưới đây), và hình che thật sự đổi điểm ảnh trong vùng, không lan ra ngoài.</summary>
public class RedactMatchesTests
{
    private static List<SKRect> Areas() => [new(10, 10, 110, 40), new(10, 60, 110, 90), new(150, 10, 250, 40)];

    private static CompositeEditCommand Redact(ObservableCollection<AnnotationShape> shapes, RedactMode mode) =>
        new(Areas().Select(a => (IEditCommand)new AddAnnotationCommand(shapes, new RedactAnnotation { Mode = mode, Bounds = a, StrokeWidth = 3 })).ToList(),
            "Che Mosaic 3 chỗ khớp \"0901\"");

    [Fact]
    public void Che_nhieu_cho_khop_la_1_buoc_Undo()
    {
        var shapes = new ObservableCollection<AnnotationShape>();
        var stack = new UndoRedoStack();

        stack.Do(Redact(shapes, RedactMode.Mosaic));

        Assert.Equal(3, shapes.Count);
        Assert.All(shapes, s => Assert.Equal(RedactMode.Mosaic, Assert.IsType<RedactAnnotation>(s).Mode));
        Assert.Equal("Che Mosaic 3 chỗ khớp \"0901\"", stack.UndoDescription);

        stack.Undo();
        Assert.Empty(shapes);
        stack.Redo();
        Assert.Equal(3, shapes.Count);
    }

    [Theory]
    // chữ 40 px (+ nới 4 px mỗi bên = 48): Size 3 không đủ → tự tăng
    [InlineData(RedactMode.Blur, 48f, 3f, 13f)]
    [InlineData(RedactMode.Mosaic, 48f, 3f, 4f)]
    // chữ nhỏ: giữ mức người dùng chọn
    [InlineData(RedactMode.Blur, 20f, 3f, 4f)]
    [InlineData(RedactMode.Mosaic, 20f, 5f, 5f)]
    // chữ rất to: kẹp ở mức tối đa của thanh Size
    [InlineData(RedactMode.Blur, 200f, 3f, 20f)]
    public void Muc_che_tu_tang_theo_chieu_cao_chu(RedactMode mode, float height, float minimum, float expected) =>
        Assert.Equal(expected, RedactAnnotation.StrengthToHide(mode, height, minimum));

    [Fact]
    public void Lam_mo_chu_to_du_kin_sigma_it_nhat_mot_phan_ba_chieu_cao()
    {
        float size = RedactAnnotation.StrengthToHide(RedactMode.Blur, 48f, 3f);
        Assert.True(3 + size >= 48f / 3f, $"sigma {3 + size} < {48f / 3f}");
    }

    [Theory]
    [InlineData(RedactMode.Mosaic)]
    [InlineData(RedactMode.Blur)]
    public void Hinh_che_doi_diem_anh_trong_vung_khong_lan_ra_ngoai(RedactMode mode)
    {
        // Ảnh sọc đen trắng 1 px (như nét chữ nhỏ) - che xong trong vùng không còn sọc, ngoài vùng giữ nguyên.
        using var image = new SKBitmap(new SKImageInfo(300, 120, SKColorType.Bgra8888, SKAlphaType.Premul));
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                image.SetPixel(x, y, x % 2 == 0 ? SKColors.Black : SKColors.White);
            }
        }
        var shape = new RedactAnnotation { Mode = mode, Bounds = new SKRect(40, 20, 140, 60), StrokeWidth = 3 };
        using var output = image.Copy();
        using (var canvas = new SKCanvas(output))
        {
            shape.Render(canvas, image);
        }

        // Trong vùng: 2 điểm cạnh nhau không còn đen / trắng tương phản.
        var a = output.GetPixel(90, 40);
        var b = output.GetPixel(91, 40);
        Assert.True(Math.Abs(a.Red - b.Red) < 60, $"trong vùng vẫn còn sọc: {a} / {b}");
        // Ngoài vùng: giữ nguyên.
        Assert.Equal(image.GetPixel(10, 100), output.GetPixel(10, 100));
        Assert.Equal(image.GetPixel(200, 40), output.GetPixel(200, 40));
    }
}
