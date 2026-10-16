using ScreenCapture.Models;
using ScreenCapture.Services;
using SkiaSharp;

namespace ScreenCapture.Tests;

/// <summary>Phiên tạm (nhớ tab khi tắt app) trong thư mục test riêng (TestSupport.SessionFolder). Các test dùng chung thư
/// mục nên chạy tuần tự trong 1 class.</summary>
public class SessionServiceTests : IDisposable
{
    public SessionServiceTests()
    {
        Assert.Equal(TestSupport.SessionFolder, SessionService.Folder); // không bao giờ ghi vào phiên thật
        Reset();
    }

    public void Dispose() => Reset();

    /// <summary>Xoá cả thư mục cha ScreenCaptureTests_… (riêng của lần chạy test này).</summary>
    private static void Reset()
    {
        var root = Path.GetDirectoryName(SessionService.Folder)!;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static SessionDocument Doc(string title, SKColor color, params AnnotationShape[] shapes) =>
        new(Guid.NewGuid(), title, TestSupport.Solid(40, 30, color), shapes, SavedToFile: false, null, null, ImageOrientation.Original);

    [Fact]
    public void Save_then_load_restores_tabs_shapes_and_active_tab()
    {
        var text = new TextAnnotation { Bounds = new SKRect(1, 2, 30, 20), Text = "Xin chào", FontSize = 18, Bold = true };
        var arrow = new LineArrowAnnotation { Bounds = new SKRect(0, 0, 25, 25), StartHead = ArrowHead.Arrow };
        var first = Doc("2026-10-16 10 01 46", SKColors.Red, new RectangleAnnotation { Bounds = new SKRect(5, 5, 15, 15), CornerRadius = 4 });
        var second = Doc("bao_cao", SKColors.Blue, text, arrow) with { SavedToFile = true, FilePath = @"C:\anh\bao_cao.png", Orientation = new ImageOrientation(1, true) };

        new SessionService().Save([first, second], first.Id);
        var (documents, activeId) = new SessionService().Load();

        Assert.Equal(first.Id, activeId);
        Assert.Equal(["2026-10-16 10 01 46", "bao_cao"], documents.Select(d => d.Title));
        Assert.Equal(SKColors.Blue, documents[1].Bitmap.GetPixel(3, 3));
        Assert.True(documents[1].SavedToFile);
        Assert.Equal(@"C:\anh\bao_cao.png", documents[1].FilePath);
        Assert.Equal(new ImageOrientation(1, true), documents[1].Orientation);
        Assert.Equal(4, Assert.IsType<RectangleAnnotation>(Assert.Single(documents[0].Shapes)).CornerRadius);
        var loadedText = Assert.IsType<TextAnnotation>(documents[1].Shapes[0]);
        Assert.Equal(("Xin chào", 18f, true), (loadedText.Text, loadedText.FontSize, loadedText.Bold));
        Assert.Equal(ArrowHead.Arrow, Assert.IsType<LineArrowAnnotation>(documents[1].Shapes[1]).StartHead);
    }

    [Fact]
    public void Keeps_only_newest_tabs_and_cleans_unused_files()
    {
        var docs = Enumerable.Range(1, 5).Select(i => Doc($"Ảnh {i}", SKColors.Green)).ToList();
        var service = new SessionService { MaxTabs = 3 };
        service.Save(docs, docs[0].Id); // tab đang mở bị cắt → chọn tab cuối
        var (loaded, activeId) = new SessionService().Load();
        Assert.Equal(["Ảnh 3", "Ảnh 4", "Ảnh 5"], loaded.Select(d => d.Title));
        Assert.Equal(docs[4].Id, activeId);
        Assert.Equal(3 + 1, Directory.GetFiles(SessionService.Folder).Length); // 3 ảnh + session.json

        service.Save([docs[4]], docs[4].Id); // đóng bớt tab → ảnh thừa bị dọn
        Assert.Equal(1 + 1, Directory.GetFiles(SessionService.Folder).Length);
    }

    [Fact]
    public void Disabled_saves_nothing_and_broken_manifest_loads_empty()
    {
        new SessionService { Enabled = false }.Save([Doc("x", SKColors.Black)], null);
        Assert.Empty(new SessionService().Load().Documents);

        File.WriteAllText(Path.Combine(SessionService.Folder, "session.json"), "{ hỏng");
        var (documents, activeId) = new SessionService().Load();
        Assert.Empty(documents);
        Assert.Null(activeId);
    }

    [Fact]
    public void Missing_image_file_drops_only_that_tab()
    {
        var a = Doc("A", SKColors.Red);
        var b = Doc("B", SKColors.Blue);
        new SessionService().Save([a, b], b.Id);
        var image = Directory.GetFiles(SessionService.Folder, $"{a.Id:N}_*.png").Single();
        File.Delete(image); // Windows dọn %TEMP%
        Assert.Equal(["B"], new SessionService().Load().Documents.Select(d => d.Title));
    }
}
