using Common.Ocr;
using ImageCompare.Engine;
using SkiaSharp;

namespace ImageCompare.Tests;

/// <summary>So chữ (Engine/TextDiff) trên dữ liệu OCR giả - không phụ thuộc OCR thật nên chạy ổn định.</summary>
public class TextDiffTests
{
    /// <summary>1 dòng OCR: mỗi ký tự CJK là 1 "từ" (như Windows OCR tiếng Nhật), chữ Latin / số liền 1 từ.
    /// Rộng 12 px / ký tự, cao 12 px.</summary>
    private static OcrLine Line(int x, int y, params string[] texts)
    {
        var words = new List<OcrWord>();
        foreach (var text in texts)
        {
            if (text == " ")
            {
                x += 30; // khoảng trống lớn = ranh giới đoạn
                continue;
            }
            int w = text.Length * 12;
            words.Add(new OcrWord(text, SKRectI.Create(x, y, w, 12), 90));
            x += w + 2;
        }
        var b = words[0].Bounds;
        foreach (var w in words)
        {
            b.Union(w.Bounds);
        }
        return new OcrLine(words, b);
    }

    private static OcrResult Ocr(params OcrLine[] lines) => new(lines, TimeSpan.Zero);

    /// <summary>Form giả: nhãn + giá trị trên lưới; <paramref name="drift"/> = B trôi dần (px mỗi hàng / cột) như khi
    /// font khác làm bố cục xê dịch.</summary>
    private static OcrResult Form((string Label, string Value)[] fields, int drift = 0, int dy0 = 0)
    {
        var lines = new List<OcrLine>();
        for (int i = 0; i < fields.Length; i++)
        {
            int row = i / 3, col = i % 3;
            int x = 20 + col * 300 + col * drift, y = 20 + row * 30 + row * drift + dy0;
            lines.Add(Line(x, y, fields[i].Label, " ", fields[i].Value));
        }
        return Ocr([.. lines]);
    }

    private static readonly (string, string)[] Fields =
    [
        ("受注番号", "0907-22-0298"), ("部門", "001111"), ("担当", "000907"),
        ("受注日付", "230107"), ("下版日", "230125"), ("出荷開始日", "230216"),
        ("受注単価", "6,800.00"), ("受注金額合計", "1,360,000"), ("検収日", "230216"),
        ("確定", "確定"), ("数量", "200"), ("納期", "確定"),
    ];

    [Fact]
    public void Key_folds_width_and_ocr_confusables()
    {
        Assert.Equal(TextDiff.Key("オーバー率"), TextDiff.Key("ｵｰﾊﾞｰ率"));
        Assert.Equal(TextDiff.Key("230107"), TextDiff.Key("23ロ1ロ7"));
        Assert.Equal(TextDiff.Key("93770-029-0002"), TextDiff.Key("93770ー029一0002"));
        Assert.Equal("受注番号", TextDiff.Key(" 受 注 番 号 "));
        Assert.Equal(TextDiff.Key("230107"), TextDiff.Key("[230107|"));
        Assert.Equal(TextDiff.Key("ABC１２３"), TextDiff.Key("abc123"));
        Assert.Equal(TextDiff.Key("1,360,000"), TextDiff.Key("1.360.000"));
        Assert.Equal(TextDiff.Key("その他金額"), TextDiff.Key("「その他金額」"));
        Assert.Equal(TextDiff.Key("その他金額"), TextDiff.Key("その他金額\\"));
    }

    [Fact]
    public void OcrText_JoinLine_drops_spaces_only_next_to_cjk()
    {
        // Windows OCR tiếng Nhật: mỗi ký tự 1 "từ" → liền; tiếng Việt / Anh giữ dấu cách, kể cả sau dấu câu.
        Assert.Equal("受注番号", OcrText.JoinLine(["受", "注", "番", "号"]));
        Assert.Equal("商品ID 1234", OcrText.JoinLine(["商", "品", "ID", "1234"]));
        Assert.Equal("Trạng thái: Đã giao", OcrText.JoinLine(["Trạng", "thái:", "Đã", "giao"]));
        Assert.Equal("受注番号", new OcrLine([new OcrWord("受注", SKRectI.Empty, 90), new OcrWord("番号", SKRectI.Empty, 90)], SKRectI.Empty).Text);
    }

    [Fact]
    public void OcrText_JoinWords_adds_spaces_only_between_latin_words()
    {
        Assert.Equal("受注番号", OcrText.JoinWords(["受", "注", "番", "号"]));
        Assert.Equal("Microsoft Edge", OcrText.JoinWords(["Microsoft", "Edge"]));
        Assert.Equal("商品ID", OcrText.JoinWords(["商", "品", "ID"]));
    }

    [Fact]
    public void Segments_split_label_and_value_at_wide_gap()
    {
        var segments = TextDiff.Segments(Ocr(Line(10, 10, "受", "注", "日", "付", " ", "230107", " ", "下", "版", "日")));
        Assert.Equal(new[] { "受注日付", "230107", "下版日" }, segments.Select(s => s.Text).ToArray());
    }

    [Fact]
    public void Same_form_with_drifting_layout_has_no_differences()
    {
        var a = Form(Fields);
        var b = Form(Fields, drift: 6, dy0: 10); // trôi dần tới vài chục px + lệch chung 10 px
        var result = TextDiff.Compare(a, b, new SKPointI(0, 10));
        Assert.Empty(result.Items);
        Assert.Equal(TextDiff.Segments(a).Count, result.SameCount);
    }

    [Fact]
    public void Changed_value_is_reported_once_with_both_texts()
    {
        var changed = Fields.ToArray();
        changed[7] = ("受注金額合計", "2,720,000");
        var result = TextDiff.Compare(Form(Fields), Form(changed, drift: 4), SKPointI.Empty);
        var item = Assert.Single(result.Items);
        Assert.Equal(TextDiffKind.Changed, item.Kind);
        Assert.Equal("1,360,000", item.A!.Text);
        Assert.Equal("2,720,000", item.B!.Text);
    }

    [Fact]
    public void Small_label_difference_is_similar_but_any_digit_change_is_changed()
    {
        var changed = Fields.ToArray();
        changed[3] = ("受注日附", "230107");   // nhãn lệch 1 chữ (OCR) → gần giống
        changed[5] = ("出荷開始日", "230218"); // giá trị lệch 1 chữ số → đổi thật
        var result = TextDiff.Compare(Form(Fields), Form(changed), SKPointI.Empty);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("受注日付", result.Items.Single(i => i.Kind == TextDiffKind.Similar).A!.Text);
        Assert.Equal("230216", result.Items.Single(i => i.Kind == TextDiffKind.Changed).A!.Text);
    }

    [Theory]
    [InlineData("受注入力-【受注基本登録】-Work-Microsoft Edge", "受注人力-【受注基本登録】-Work-Microsoft Edgc", true)]
    [InlineData("230216", "230218", false)]
    [InlineData("1,360,000", "2,720,000", false)]
    [InlineData("確定", "未定", false)] // quá ngắn: 2 ký tự khác 1 = khác thật
    public void Likely_ocr_noise(string a, string b, bool expected) =>
        Assert.Equal(expected, TextDiff.IsLikelyOcrNoise(TextDiff.Key(a), TextDiff.Key(b)));

    [Fact]
    public void Missing_and_extra_fields_are_only_in_a_or_b()
    {
        var a = Ocr([.. Form(Fields).Lines, Line(20, 400, "企", "画", "作", "業", "の", "み")]);
        var b = Ocr([.. Form(Fields).Lines, Line(600, 400, "生", "産", "記", "録")]);
        var result = TextDiff.Compare(a, b, SKPointI.Empty);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("企画作業のみ", result.Items.Single(i => i.Kind == TextDiffKind.OnlyInA).A!.Text);
        Assert.Equal("生産記録", result.Items.Single(i => i.Kind == TextDiffKind.OnlyInB).B!.Text);
    }

    [Fact]
    public void Segment_split_differently_in_b_still_matches()
    {
        // A: OCR gộp nhãn + giá trị thành 1 đoạn; B: tách thành 2 đoạn.
        var a = Ocr(Line(20, 20, "受", "注", "日", "付", "230107"));
        var b = Ocr(Line(24, 22, "受", "注", "日", "付", " ", "230107"));
        var result = TextDiff.Compare(a, b, SKPointI.Empty);
        Assert.Empty(result.Items);
        Assert.Equal(1, result.SameCount);
    }

    [Fact]
    public void Segments_split_at_box_border_between_label_and_value()
    {
        // "受注区分" trên nền trang, "受注" trong combobox sát bên: khoảng trống nhỏ, chỉ viền ô ngăn cách.
        var ocr = Ocr(Line(10, 10, "受", "注", "区", "分", "受", "注"));
        var bmp = Images.New(200, 40, new SKColor(0xD8, 0xE4, 0xF0));
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.DrawRect(SKRect.Create(65, 6, 80, 20), new SKPaint { Color = SKColors.White });
            canvas.DrawRect(SKRect.Create(65, 6, 1, 20), new SKPaint { Color = new SKColor(0x70, 0x70, 0x70) });
        }
        Assert.Single(TextDiff.Segments(ocr));
        Assert.Equal(["受注区分", "受注"], TextDiff.Segments(ocr, bmp).Select(s => s.Text));

        // Nét dọc chỉ cao bằng dòng chữ (nét ký tự mà khung từ OCR bỏ sót) thì không phải viền.
        var stroke = Images.New(200, 40, SKColors.White);
        using (var canvas = new SKCanvas(stroke))
        {
            canvas.DrawRect(SKRect.Create(65, 10, 1, 12), new SKPaint { Color = SKColors.Black });
        }
        Assert.Single(TextDiff.Segments(ocr, stroke));
    }

    [Fact]
    public void Fragment_inside_the_other_sides_box_joins_its_pair()
    {
        // A: OCR cắt "ページ数" thành "へ" + "ゾ数" (khoảng trống 11 px > ngưỡng tách); B đọc liền "へ叮ゾ数".
        var a = Ocr(new OcrLine([new OcrWord("へ", SKRectI.Create(20, 24, 12, 6), 90),
            new OcrWord("ゾ", SKRectI.Create(43, 20, 12, 12), 90), new OcrWord("数", SKRectI.Create(57, 20, 12, 12), 90)],
            SKRectI.Create(20, 20, 49, 12)));
        var b = Ocr(Line(20, 20, "へ", "叮", "ゾ", "数"));
        var item = Assert.Single(TextDiff.Compare(a, b, SKPointI.Empty).Items);
        Assert.Equal("へゾ数", item.A!.Text);
        Assert.Equal(TextDiffKind.Similar, item.Kind);
    }

    [Fact]
    public void Repeated_labels_pair_with_the_nearest_copy()
    {
        // "確定" lặp 4 lần ở 4 chỗ; B trôi 25 px - không được ghép chéo sang chỗ khác.
        var lines = Enumerable.Range(0, 4).Select(i => Line(20, 20 + i * 40, "確", "定")).ToArray();
        var linesB = Enumerable.Range(0, 4).Select(i => Line(45, 28 + i * 40, "確", "定")).ToArray();
        var result = TextDiff.Compare(Ocr([.. lines, Line(300, 20, "仕", "様")]), Ocr([.. linesB, Line(325, 28, "仕", "様")]), SKPointI.Empty);
        Assert.Empty(result.Items);
        Assert.Equal(5, result.SameCount);
    }

    [Fact]
    public void Ignore_rects_hide_segments_inside_them()
    {
        var a = Ocr([.. Form(Fields).Lines, Line(20, 400, "1", "0", ":", "4", "5")]);
        var b = Ocr([.. Form(Fields).Lines, Line(20, 400, "1", "1", ":", "0", "2")]);
        var result = TextDiff.Compare(a, b, SKPointI.Empty, [SKRectI.Create(0, 390, 200, 40)]);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void Grey_text_vs_black_text_is_color_changed()
    {
        var bmpA = Images.New(400, 100, SKColors.White);
        var bmpB = Images.New(400, 100, SKColors.White);
        DrawText(bmpA, "確定", 20, 40, new SKColor(0x80, 0x80, 0x80));
        DrawText(bmpB, "確定", 20, 40, SKColors.Black);
        DrawText(bmpA, "数量", 200, 40, SKColors.Black);
        DrawText(bmpB, "数量", 200, 40, SKColors.Black);
        var a = Ocr(Line(20, 26, "確", "定"), Line(200, 26, "数", "量"));
        var result = TextDiff.Compare(a, a, SKPointI.Empty, null, bmpA, bmpB);
        var item = Assert.Single(result.Items);
        Assert.Equal(TextDiffKind.ColorChanged, item.Kind);
        Assert.Equal("確定", item.A!.Text);
        Assert.Equal(1, result.SameCount);
    }

    [Fact]
    public void Ink_color_ignores_antialiased_edges()
    {
        var bmp = Images.New(200, 60, SKColors.White);
        DrawText(bmp, "受注番号", 10, 40, SKColors.Black);
        var color = TextDiff.InkColor(bmp, SKRectI.Create(5, 20, 120, 30));
        Assert.NotNull(color);
        Assert.True(color!.Value.Red < 60, $"lõi nét chữ đen phải tối, được {color}");
    }

    internal static void DrawText(SKBitmap bmp, string text, float x, float y, SKColor color, float size = 14)
    {
        using var canvas = new SKCanvas(bmp);
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            TextSize = size,
            Typeface = SKFontManager.Default.MatchCharacter(text[0]) ?? SKTypeface.Default,
        };
        canvas.DrawText(text, x, y, paint);
    }
}

/// <summary>Đọc lại riêng từng chỗ nghi khác (Engine/TextDiffVerifier) - bộ đọc giả trả chữ định sẵn.</summary>
public class TextDiffVerifierTests
{
    /// <summary>Bộ đọc giả: vùng cắt từ ảnh A (nền trắng) đọc ra <paramref name="textA"/>, từ ảnh B (nền xám nhạt) ra
    /// <paramref name="textB"/> - như OCR đọc lại riêng từng vùng.</summary>
    private sealed class FixedReader(string textA, string? textB = null) : IFormTextReader
    {
        public int Calls { get; private set; }
        public string Name => "fixed";
        public OcrResult Read(SKBitmap bitmap, CancellationToken ct = default, IProgress<double>? progress = null) => new([], TimeSpan.Zero);
        public string ReadText(SKBitmap crop, int scale, int? threshold = null)
        {
            Calls++;
            return crop.GetPixel(crop.Width / 2, crop.Height / 2).Red == 0xFF ? textA : textB ?? textA;
        }
    }

    /// <summary>Như <see cref="FixedReader"/> nhưng chọn theo màu nền ở góc vùng cắt (vùng rộng thì giữa vùng có thể trúng nét
    /// chữ): nền trắng = ảnh A.</summary>
    private sealed class BackgroundReader(string textA, string textB) : IFormTextReader
    {
        public string Name => "background";
        public OcrResult Read(SKBitmap bitmap, CancellationToken ct = default, IProgress<double>? progress = null) => new([], TimeSpan.Zero);
        public string ReadText(SKBitmap crop, int scale, int? threshold = null) => crop.GetPixel(0, 0).Red == 0xFF ? textA : textB;
    }

    private static TextSegment Seg(string text, int x, int y) => new(text, TextDiff.Key(text), SKRectI.Create(x, y, text.Length * 12, 12));

    private static readonly SKBitmap Blank = Images.New(400, 200, SKColors.White);
    private static readonly SKBitmap BlankB = Images.New(400, 200, new SKColor(0xF0, 0xF0, 0xF0));

    [Fact]
    public void Misread_that_reads_the_same_when_cropped_is_dropped()
    {
        var diff = new TextDiffResult([new TextDiffItem(1, TextDiffKind.Changed, Seg("230川7", 20, 20), Seg("230107", 22, 24))], 5, 5, 4, SKPointI.Empty);
        var result = TextDiffVerifier.Verify(diff, Blank, Blank, [new FixedReader("230107")]);
        Assert.Empty(result.Items);
        Assert.Equal(5, result.SameCount);
        Assert.Equal(1, result.VerifiedSame);
    }

    [Fact]
    public void Real_change_stays_and_keeps_numbering()
    {
        var diff = new TextDiffResult(
        [
            new TextDiffItem(1, TextDiffKind.Changed, Seg("230川7", 20, 20), Seg("230107", 22, 24)),
            new TextDiffItem(2, TextDiffKind.Changed, Seg("1,360,000", 20, 60), Seg("2,720,000", 22, 64)),
        ], 5, 5, 3, SKPointI.Empty);
        // Đọc lại: A ra "230107" ở mọi vùng, B cũng vậy → mục 1 khớp; mục 2 "230107" không na ná "1,360,000" /
        // "2,720,000" (như dính nhãn bên cạnh) → không được coi là giống.
        var reader = new FixedReader("230107");
        var result = TextDiffVerifier.Verify(diff, Blank, BlankB, [reader]);
        var item = Assert.Single(result.Items);
        Assert.Equal(1, item.Number);
        Assert.Equal(TextDiffKind.Changed, item.Kind);
        Assert.Equal("1,360,000", item.A!.Text);
        Assert.True(reader.Calls > 0);
    }

    [Fact]
    public void Same_neighbour_label_read_on_both_sides_does_not_hide_a_real_change()
    {
        var diff = new TextDiffResult([new TextDiffItem(1, TextDiffKind.Changed, Seg("1,360,000", 20, 60), Seg("2,720,000", 22, 64))], 5, 5, 4, SKPointI.Empty);
        var result = TextDiffVerifier.Verify(diff, Blank, BlankB, [new FixedReader("受注金額合計")]);
        Assert.Equal(TextDiffKind.Changed, Assert.Single(result.Items).Kind);
    }

    [Fact]
    public void Only_in_b_found_in_a_by_reading_the_predicted_spot()
    {
        // Ô bị khoá chữ xám ở A: đọc cả trang không thấy, đọc lại vùng dự đoán thì ra (kèm mũi tên dropdown "v").
        var diff = new TextDiffResult([new TextDiffItem(1, TextDiffKind.OnlyInB, null, Seg("A4縦", 80, 90), Other: SKRectI.Create(84, 120, 36, 12))], 5, 6, 5, SKPointI.Empty);
        var result = TextDiffVerifier.Verify(diff, Blank, Blank, [new FixedReader("A4縦  v")]);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void Partly_read_grey_text_that_reads_the_same_is_a_color_change()
    {
        // Ô bị khoá ở A: "受注" xám, OCR cả trang chỉ ra "三"; B chữ đen. Đọc lại 2 phía cùng ra "受注" → khác màu chữ.
        var a = Images.New(200, 60, SKColors.White);
        var b = Images.New(200, 60, SKColors.White);
        TextDiffTests.DrawText(a, "受注", 20, 32, new SKColor(0xA0, 0xA0, 0xA0));
        TextDiffTests.DrawText(b, "受注", 20, 32, SKColors.Black);
        var diff = new TextDiffResult([new TextDiffItem(1, TextDiffKind.Changed, Seg("三", 26, 20), Seg("受注", 20, 20))], 5, 5, 4, SKPointI.Empty);
        var item = Assert.Single(TextDiffVerifier.Verify(diff, a, b, [new FixedReader("受注")]).Items);
        Assert.Equal(TextDiffKind.ColorChanged, item.Kind);
        Assert.Equal("受注", item.A!.Text);
    }

    [Fact]
    public void Widened_crop_may_read_a_neighbour_label_too()
    {
        // A "商印そ" hẹp hơn B → vùng đọc lại ở A được nới, dính nhãn "区分" bên trái: vẫn là cùng chữ.
        var diff = new TextDiffResult([new TextDiffItem(1, TextDiffKind.Changed, Seg("商印そ", 60, 20), Seg("商印その他", 50, 24))], 5, 5, 4, SKPointI.Empty);
        Assert.Empty(TextDiffVerifier.Verify(diff, Blank, BlankB, [new FixedReader("区分商印その他", "商印その他")]).Items);

        // Chữ dư ở phía vốn rộng hơn (không nới) là chữ thêm thật; phần dư là số cũng vậy ("200" → "1200").
        var added = new TextDiffResult([new TextDiffItem(1, TextDiffKind.Changed, Seg("商印その", 60, 20), Seg("商印その他", 60, 24))], 5, 5, 4, SKPointI.Empty);
        Assert.Single(TextDiffVerifier.Verify(added, Blank, BlankB, [new FixedReader("商印その", "商印その他")]).Items);
        var digit = new TextDiffResult([new TextDiffItem(1, TextDiffKind.Changed, Seg("00", 60, 20), Seg("200", 60, 24))], 5, 5, 4, SKPointI.Empty);
        Assert.Single(TextDiffVerifier.Verify(digit, Blank, BlankB, [new FixedReader("1200", "200")]).Items);
    }

    [Fact]
    public void Same_glyphs_with_drifting_letter_spacing_have_the_same_shape()
    {
        // B dựng rộng hơn 1 px mỗi chữ (khác trình duyệt), A chữ xám; chữ khác hẳn thì không trùng.
        var a = Images.New(200, 40, SKColors.White);
        var b = Images.New(200, 40, SKColors.White);
        var c = Images.New(200, 40, SKColors.White);
        string text = "胴サイズ";
        for (int i = 0; i < text.Length; i++)
        {
            TextDiffTests.DrawText(a, text[i].ToString(), 20 + i * 14, 26, new SKColor(0xA0, 0xA0, 0xA0));
            TextDiffTests.DrawText(b, text[i].ToString(), 23 + i * 15, 27, SKColors.Black);
            TextDiffTests.DrawText(c, "版指示確"[i].ToString(), 23 + i * 15, 27, SKColors.Black);
        }
        var boxA = SKRectI.Create(18, 10, 60, 20);
        Assert.NotNull(TextDiffVerifier.SameShape(a, boxA, b, SKRectI.Create(20, 11, 62, 20)));
        Assert.Null(TextDiffVerifier.SameShape(a, boxA, c, SKRectI.Create(20, 11, 62, 20)));
    }

    [Fact]
    public void Only_in_a_whose_glyph_matches_the_predicted_spot_is_a_color_change()
    {
        // A: "枚" xám đọc thành "物"; B: "枚" đen OCR cả trang bỏ sót. Đọc lại không khớp chữ nhưng nét trùng hình.
        var a = Images.New(200, 60, SKColors.White);
        var b = Images.New(200, 60, SKColors.White);
        TextDiffTests.DrawText(a, "枚", 40, 32, new SKColor(0xA0, 0xA0, 0xA0));
        TextDiffTests.DrawText(b, "枚", 45, 34, SKColors.Black);
        var diff = new TextDiffResult([new TextDiffItem(1, TextDiffKind.OnlyInA, Seg("物", 39, 19), null, Other: SKRectI.Create(42, 21, 12, 12))], 5, 5, 4, SKPointI.Empty);
        var item = Assert.Single(TextDiffVerifier.Verify(diff, a, b, [new FixedReader("x")]).Items);
        Assert.Equal(TextDiffKind.ColorChanged, item.Kind);
    }

    [Fact]
    public void Color_changes_are_not_reread()
    {
        var reader = new FixedReader("x");
        var diff = new TextDiffResult([new TextDiffItem(1, TextDiffKind.ColorChanged, Seg("確定", 20, 20), Seg("確定", 22, 24), "#A0A0A0 → #000000")], 5, 5, 4, SKPointI.Empty);
        var result = TextDiffVerifier.Verify(diff, Blank, Blank, [reader]);
        Assert.Equal(TextDiffKind.ColorChanged, Assert.Single(result.Items).Kind);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public void Remaining_change_shows_the_closest_readings_instead_of_garbage()
    {
        var diff = new TextDiffResult([new TextDiffItem(1, TextDiffKind.Changed, Seg("厓途区分", 20, 20), Seg("用途区分商印", 22, 24))], 5, 5, 4, SKPointI.Empty);
        var result = TextDiffVerifier.Verify(diff, Blank, BlankB, [new FixedReader("用途区分", "用途区分商印")]);
        var item = Assert.Single(result.Items);
        Assert.Equal("用途区分", item.A!.Text); // cách đọc lại sát B nhất thay cho chữ rác
        Assert.Equal("用途区分商印", item.B!.Text);
    }

    [Fact]
    public void Misread_both_sides_with_identical_glyphs_is_marked_same_glyphs()
    {
        // Ô bị khoá IE mode ↔ Edge (2026-10-07): "検査Ｓ１" xám cả 2 phía, OCR đọc A "桝査こ", B "梹査。" kể cả khi đọc lại.
        var a = Images.New(200, 60, SKColors.White);
        var b = Images.New(200, 60, new SKColor(0xF0, 0xF0, 0xF0)); // nền khác → FixedReader trả cách đọc thứ 2
        var c = Images.New(200, 60, new SKColor(0xF0, 0xF0, 0xF0));
        var grey = new SKColor(0x90, 0x90, 0x90);
        TextDiffTests.DrawText(a, "検査Ｓ１", 20, 32, grey);
        TextDiffTests.DrawText(b, "検査Ｓ１", 22, 34, grey);
        TextDiffTests.DrawText(c, "検査Ｓ２", 22, 34, grey);
        var reader = new BackgroundReader("桝査こ", "梹査。");

        // Khung OCR bao cả 4 chữ (OCR đọc ra 3 ký tự nhưng khung vẫn phủ cả dòng chữ).
        var segA = new TextSegment("桝査こ", TextDiff.Key("桝査こ"), SKRectI.Create(18, 18, 58, 16));
        var segB = new TextSegment("梹査。", TextDiff.Key("梹査。"), SKRectI.Create(20, 20, 58, 16));
        var same = new TextDiffResult([new TextDiffItem(1, TextDiffKind.Changed, segA, segB)], 5, 5, 4, SKPointI.Empty);
        Assert.True(Assert.Single(TextDiffVerifier.Verify(same, a, b, [reader]).Items).SameGlyphs);

        // Khác 1 chữ thật ("１" → "２") thì không đánh dấu - vẫn hiện như khác.
        Assert.False(Assert.Single(TextDiffVerifier.Verify(same, a, c, [reader]).Items).SameGlyphs);
    }

    [Theory]
    [InlineData("厚物・薄物共通", 0x00, false, true)]   // cùng chữ, cùng màu → ≡ (So nét chữ ẩn)
    [InlineData("厚物・薄物専用", 0x00, false, false)]  // khác chữ thật → giữ "chỉ có ở B"
    [InlineData("厚物・薄物共通", 0x60, true, false)]   // cùng chữ, A xám ↔ B đen → khác màu chữ
    public void Only_in_b_next_to_a_cell_border_in_a_is_checked_by_glyph_shape(string textA, byte grey, bool colorChange, bool sameGlyphs)
    {
        // Tesseract (IE mode ↔ Edge, 2026-10-08): "厚物・薄物共通" chỉ đọc ra ở B; ở A có vạch viền ô sát trái chữ nên
        // SameShape (nới vùng dự đoán 3 px) dính vạch → khung nét lệch cỡ → không trùng.
        var a = Images.New(220, 50, SKColors.White);
        var b = Images.New(220, 50, new SKColor(0xF0, 0xF0, 0xF0)); // nền khác → BackgroundReader đọc 2 phía 2 kiểu
        using (var canvas = new SKCanvas(a))
        using (var line = new SKPaint { Color = new SKColor(0x40, 0x40, 0x40), StrokeWidth = 1 })
        {
            canvas.DrawLine(19.5f, 2, 19.5f, 48, line);
        }
        TextDiffTests.DrawText(a, textA, 22, 32, new SKColor(grey, grey, grey));
        TextDiffTests.DrawText(b, "厚物・薄物共通", 20, 30, SKColors.Black);
        var segB = new TextSegment("厚物・薄物共通", TextDiff.Key("厚物・薄物共通"), SKRectI.Create(19, 15, 100, 19));
        var predicted = SKRectI.Create(21, 17, 100, 19);
        Assert.Null(TextDiffVerifier.SameShape(b, segB.Bounds, a, predicted)); // tiền đề: cách so cũ không bắt được

        var diff = new TextDiffResult([new TextDiffItem(1, TextDiffKind.OnlyInB, null, segB, Other: predicted)], 5, 5, 4, SKPointI.Empty);
        var item = Assert.Single(TextDiffVerifier.Verify(diff, a, b, [new BackgroundReader("xa", "xb")]).Items);

        Assert.Equal(colorChange ? TextDiffKind.ColorChanged : TextDiffKind.OnlyInB, item.Kind);
        Assert.Equal(sameGlyphs, item.SameGlyphs);
    }

    [Fact]
    public void Strict_shape_check_catches_one_changed_digit_in_a_long_line()
    {
        // Dòng dài chỉ khác 1 chữ số: tổng pixel lệch nhỏ so với cả dòng, nhưng khung quanh chữ số đó lệch nhiều.
        var a = Images.New(300, 40, SKColors.White);
        var b = Images.New(300, 40, SKColors.White);
        var c = Images.New(300, 40, SKColors.White);
        TextDiffTests.DrawText(a, "受注合計数量 1,360,200", 10, 26, SKColors.Black);
        TextDiffTests.DrawText(b, "受注合計数量 1,360,200", 11, 27, SKColors.Black);
        TextDiffTests.DrawText(c, "受注合計数量 1,360,300", 11, 27, SKColors.Black);
        var box = SKRectI.Create(8, 10, 200, 20);
        var near = SKRectI.Create(9, 11, 200, 20);
        Assert.True(TextDiffVerifier.SameShapeStrict(a, box, b, near));
        Assert.False(TextDiffVerifier.SameShapeStrict(a, box, c, near));
    }
}

/// <summary>Xử lý ảnh form trước OCR (Engine/FormPreprocess).</summary>
public class FormPreprocessTests
{
    [Fact]
    public void Box_borders_are_removed_text_is_kept()
    {
        var bmp = Images.New(300, 60, SKColors.White);
        using (var canvas = new SKCanvas(bmp))
        using (var stroke = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 1 })
        {
            canvas.DrawRect(SKRect.Create(10.5f, 10.5f, 200, 24), stroke); // viền ô nhập
        }
        TextDiffTests.DrawText(bmp, "230107", 20, 28, SKColors.Black);

        var image = FormPreprocess.Prepare(bmp, 3);
        Assert.Equal(900, image.Width);
        Assert.False(image.IsInk(150 * 3, 10 * 3 + 1), "viền trên phải bị xoá");
        Assert.False(image.IsInk(10 * 3 + 1, 22 * 3), "viền trái phải bị xoá");
        int ink = image.Pixels.Count(p => p == 0);
        Assert.True(ink > 200, $"chữ phải còn lại, chỉ còn {ink} px mực");
    }

    [Fact]
    public void Faint_grey_text_survives_and_light_backgrounds_do_not_become_ink()
    {
        // Vùng cắt chỉ gồm nền trang xanh nhạt + ô trắng + chữ xám nét mảnh (ô bị khoá).
        var bmp = Images.New(120, 30, new SKColor(0xEE, 0xF9, 0xFF));
        using (var canvas = new SKCanvas(bmp))
        using (var white = new SKPaint { Color = SKColors.White })
        {
            canvas.DrawRect(SKRect.Create(40, 0, 80, 30), white);
        }
        using (var canvas = new SKCanvas(bmp))
        using (var grey = new SKPaint { Color = new SKColor(0xA0, 0xA0, 0xA0), IsAntialias = false, StrokeWidth = 1 })
        {
            for (int x = 50; x < 110; x += 6)
            {
                canvas.DrawLine(x + 0.5f, 8, x + 0.5f, 22, grey); // nét dọc 1 px như chữ bitmap
            }
        }
        var normal = FormPreprocess.Prepare(bmp, 3);
        var high = FormPreprocess.Prepare(bmp, 3, threshold: 225);
        int Ink(BinaryImage img, int x0, int x1) => Enumerable.Range(0, img.Height).Sum(y => Enumerable.Range(x0, x1 - x0).Count(x => img.IsInk(x, y)));
        Assert.Equal(0, Ink(normal, 0, 40 * 3)); // nền xanh nhạt không thành mực
        Assert.True(Ink(normal, 50 * 3, 110 * 3) > 0, "chữ xám phải còn ở ngưỡng thường");
        Assert.True(Ink(high, 50 * 3, 110 * 3) > Ink(normal, 50 * 3, 110 * 3), "ngưỡng cao giữ nét xám dày hơn");
    }

    [Fact]
    public void Text_blobs_separate_label_and_far_value()
    {
        var bmp = Images.New(400, 50, SKColors.White);
        TextDiffTests.DrawText(bmp, "受注日付", 10, 30, SKColors.Black);
        TextDiffTests.DrawText(bmp, "230107", 250, 30, SKColors.Black);
        var blobs = FormPreprocess.TextBlobs(FormPreprocess.Prepare(bmp, 3));
        Assert.Equal(2, blobs.Count);
        Assert.True(blobs[0].Right / 3 < 100 && blobs[1].Left / 3 > 240);
    }
}

/// <summary>Đọc form bằng Tesseract (dự phòng tiếng Nhật, và tiếng Việt / Anh) trên ảnh form tự vẽ.</summary>
public class FormReaderTests
{
    /// <summary>Ảnh form: mỗi dòng 1 nhãn + 1 ô nhập có viền chứa giá trị (như màn hình nghiệp vụ).</summary>
    private static SKBitmap FormImage((string Label, string Value)[] rows, float size = 13)
    {
        var bmp = Images.New(520, 30 + rows.Length * 28, new SKColor(0xF0, 0xF8, 0xFF));
        using var canvas = new SKCanvas(bmp);
        using var stroke = new SKPaint { Color = new SKColor(0x70, 0x70, 0x70), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var fill = new SKPaint { Color = SKColors.White };
        for (int i = 0; i < rows.Length; i++)
        {
            float y = 15 + i * 28;
            TextDiffTests.DrawText(bmp, rows[i].Label, 12, y + 15, SKColors.Black, size);
            var box = SKRect.Create(200.5f, y, 220, 21);
            canvas.DrawRect(box, fill);
            canvas.DrawRect(box, stroke);
            TextDiffTests.DrawText(bmp, rows[i].Value, 206, y + 15, SKColors.Black, size);
        }
        return bmp;
    }

    private static int Found(OcrResult ocr, IEnumerable<string> expected)
    {
        var keys = TextDiff.Segments(ocr).Select(s => s.Key).ToList();
        string all = string.Concat(keys);
        return expected.Count(e => all.Contains(TextDiff.Key(e), StringComparison.Ordinal));
    }

    [Fact]
    public void Tesseract_reads_japanese_form()
    {
        if (SKFontManager.Default.MatchCharacter('受') is null)
        {
            Assert.Skip("Máy không có font tiếng Nhật để dựng ảnh thử");
        }
        (string, string)[] rows = [("受注番号", "0907-22-0298"), ("商品コード", "93770-029-0002"), ("出荷開始日", "230216"), ("受注単価", "6,800.00"), ("製品名", "環境白書")];
        using var bmp = FormImage(rows);
        var ocr = new TesseractFormReader("jpn", "test").Read(bmp, TestContext.Current.CancellationToken);
        int found = Found(ocr, rows.SelectMany(r => new[] { r.Item1, r.Item2 }));
        Assert.True(found >= 7, $"chỉ đọc đúng {found}/10: {string.Join(" | ", ocr.Lines.Select(l => l.Text))}");
    }

    [Fact]
    public void Tesseract_reads_vietnamese_form()
    {
        (string, string)[] rows = [("Khách hàng", "Công ty Minh Long"), ("Mã đơn", "DH-20250115"), ("Ngày giao", "15/01/2025"), ("Trạng thái", "Đã giao")];
        using var bmp = FormImage(rows, 14);
        var ocr = new TesseractFormReader("vie", "test").Read(bmp, TestContext.Current.CancellationToken);
        int found = Found(ocr, rows.SelectMany(r => new[] { r.Item1, r.Item2 }));
        Assert.True(found >= 6, $"chỉ đọc đúng {found}/8: {string.Join(" | ", ocr.Lines.Select(l => l.Text))}");
    }

    [Fact]
    public void End_to_end_changed_value_is_found_between_two_renderings()
    {
        if (SKFontManager.Default.MatchCharacter('受') is null)
        {
            Assert.Skip("Máy không có font tiếng Nhật để dựng ảnh thử");
        }
        (string, string)[] rowsA = [("受注番号", "0907-22-0298"), ("受注数量", "200"), ("受注金額合計", "1,360,000"), ("検収日", "230216")];
        var rowsB = rowsA.ToArray();
        rowsB[2] = ("受注金額合計", "2,720,000");
        using var a = FormImage(rowsA, 13);
        using var b = FormImage(rowsB, 14); // B: chữ to hơn 1 cỡ như khác trình duyệt
        var reader = new TesseractFormReader("jpn", "test");
        var ct = TestContext.Current.CancellationToken;
        var ocrA = reader.Read(a, ct);
        var ocrB = reader.Read(b, ct);
        var result = TextDiff.Compare(ocrA, ocrB, SKPointI.Empty, null, a, b, ct);
        string dump = $"A: {string.Join(" | ", TextDiff.Segments(ocrA).Select(s => $"{s.Text}@{s.Bounds}"))}\n"
            + $"B: {string.Join(" | ", TextDiff.Segments(ocrB).Select(s => $"{s.Text}@{s.Bounds}"))}\n"
            + $"items: {string.Join(" | ", result.Items.Select(i => $"{i.Kind}:{i.A?.Text}->{i.B?.Text}"))}";
        Assert.True(result.Items.Any(i => i.Kind is TextDiffKind.Changed or TextDiffKind.OnlyInB
            && TextDiff.Key(i.B!.Text).Contains(TextDiff.Key("2,720,000"), StringComparison.Ordinal)), dump);
        Assert.True(result.SameCount >= 4, $"phần giống nhau phải ghép được, chỉ {result.SameCount}\n{dump}");
    }
}
