using Common.Ocr;
using SkiaSharp;

namespace ImageCompare.Tests;

/// <summary>1 OcrEngine chỉ chạy được 1 RecognizeAsync cùng lúc. Lỗi đã gặp (Tìm chữ của ScreenCapture, gõ "日" qua IME): huỷ
/// lần đọc đang chạy rồi đọc lại ngay → "Another RecognizeAsync operation is already running" (E_ABORT). Cần OCR tiếng Nhật
/// của Windows - máy không có thì bỏ qua.</summary>
public class WindowsOcrConcurrencyTests
{
    private static WindowsFormReader Reader() =>
        WindowsFormReader.TryCreate("ja", "Windows OCR (tiếng Nhật)")
        ?? throw new InvalidOperationException("skip");

    /// <summary>Ảnh nhiều dòng chữ Nhật, đủ cao để Windows OCR đọc thành nhiều dải (mỗi dải 1 lần RecognizeAsync).</summary>
    private static SKBitmap JapanesePage(int lines)
    {
        using var typeface = SKFontManager.Default.MatchCharacter('日'); // font có chữ Nhật (Yu Gothic / Meiryo...)
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true, TextSize = 28, Typeface = typeface };
        var bmp = new SKBitmap(new SKImageInfo(900, 40 + 50 * lines, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        for (int i = 0; i < lines; i++)
        {
            canvas.DrawText($"{i + 1}月{i % 28 + 1}日 予定組画面 工数 稼働率 受注番号 {1000 + i}", 20, 50 + 50 * i, paint);
        }
        return bmp;
    }

    private static bool HasJapaneseOcr() => WindowsFormReader.TryCreate("ja", "x") is not null;

    [Fact]
    public async Task Huy_lan_doc_dang_chay_roi_doc_lai_ngay_khong_loi()
    {
        Assert.SkipUnless(HasJapaneseOcr(), "Máy không có OCR tiếng Nhật của Windows");
        var reader = Reader();
        using var page = JapanesePage(220); // ~11.000 px → nhiều dải

        using var cts = new CancellationTokenSource();
        var first = Task.Run(() => reader.Read(page, cts.Token));
        await Task.Delay(150);
        cts.Cancel();
        var second = await Task.Run(() => reader.Read(page));

        Assert.NotEmpty(second.Lines);
        // Lần đầu: bị huỷ (OperationCanceledException) hoặc đã xong trước khi huỷ - không được là lỗi E_ABORT của Windows.
        var error = await Record.ExceptionAsync(() => first);
        Assert.True(error is null or OperationCanceledException, error?.ToString());
    }

    [Fact]
    public async Task Nhieu_lan_doc_song_song_tren_cung_engine_deu_xong()
    {
        Assert.SkipUnless(HasJapaneseOcr(), "Máy không có OCR tiếng Nhật của Windows");
        var reader = Reader();
        using var page = JapanesePage(12);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => reader.Read(page))));

        // Test cách chạy đồng thời, không đo độ đúng của OCR: cả 4 lần đều xong và ra cùng 1 kết quả.
        Assert.NotEmpty(results[0].Lines);
        Assert.All(results, r => Assert.Equal(results[0].FullText, r.FullText));
    }
}
