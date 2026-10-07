using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageCompare.Engine;
using ImageCompare.Models;
using ImageCompare.Services;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace ImageCompare.ViewModels;

/// <summary>1 dòng trong danh sách kết quả bên phải: vùng khác (chế độ Khác biệt) hoặc chỗ tìm thấy (Tìm ảnh con).
/// <see cref="Bounds"/> theo toạ độ vẽ trên canvas của chế độ đó - bấm vào để phóng tới.</summary>
public sealed record ResultItem(int Number, string Text, SKRectI Bounds, string BadgeColor);

/// <summary>Trạng thái so sánh: 2 ảnh, chế độ xem, tuỳ chọn và kết quả. Không tham chiếu kiểu WinUI -
/// MainWindow (code-behind) lo hộp thoại, clipboard, vẽ canvas và thao tác chuột (cùng cách chia như
/// EditorWindow / EditorViewModel của ScreenCapture). So sánh / tìm chạy nền, đổi ảnh / tuỳ chọn thì huỷ lượt cũ.</summary>
public sealed partial class CompareViewModel : ObservableObject
{
    private static readonly ILogger Log = AppLog.For(nameof(CompareViewModel));
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private CancellationTokenSource? _compareCts;
    private CancellationTokenSource? _findCts;

    private CancellationTokenSource? _ocrCts;
    private string? _ocrError; // lỗi lần đọc chữ gần nhất - giữ lại khi bảng bên phải vẽ lại

    /// <summary>Chữ đã đọc của từng ảnh: đổi qua lại A / B, chế độ khác rồi quay lại không phải đọc lại.</summary>
    private readonly ConditionalWeakTable<LoadedImage, OcrResult> _ocrCache = new();

    private CancellationTokenSource? _textDiffCts;
    private string? _textDiffError;
    /// <summary>Bộ đọc của lần Tìm chữ gần nhất + ghi chú khi phải dùng dự phòng (hiện ở bảng kết quả).</summary>
    private string? _textReaderName;
    private string? _textReaderNote;
    private string? _textDiffReaderName;
    private string? _textDiffReaderNote;

    /// <summary>Chữ đọc theo kiểu form (chế độ So chữ) của từng ảnh, theo từng ngôn ngữ - khác cách đọc của Tìm chữ
    /// nên cache riêng.</summary>
    private readonly ConditionalWeakTable<LoadedImage, Dictionary<FormLanguage, OcrResult>> _formOcrCache = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImageAText), nameof(HasBothImages), nameof(TextTarget))]
    private LoadedImage? _imageA;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImageBText), nameof(HasBothImages), nameof(TextTarget))]
    private LoadedImage? _imageB;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport), nameof(CanExportTextDiff))]
    private ViewMode _mode = ViewMode.Diff;

    /// <summary>Độ đục của ảnh B ở chế độ Chồng mờ (0 = chỉ thấy A, 1 = chỉ thấy B).</summary>
    [ObservableProperty]
    private double _overlayOpacity = 0.5;

    /// <summary>Vị trí vạch chia ở chế độ Thanh trượt, theo toạ độ ảnh (px tính từ mép trái ảnh A).</summary>
    [ObservableProperty]
    private double _swipeX;

    /// <summary>Vị trí ảnh B so với ảnh A (px ảnh A) - lấy từ kết quả căn chỉnh, dùng cho các chế độ xem khác.</summary>
    [ObservableProperty]
    private SKPointI _offsetB;

    [ObservableProperty]
    private string _statusText = "Mở hoặc dán 2 ảnh để so sánh (kéo-thả file, Ctrl+V, hoặc nút Mở / Dán).";

    // ---- Tuỳ chọn so sánh ----
    [ObservableProperty]
    private AlignMode _align = AlignMode.Translate;

    [ObservableProperty]
    private double _thresholdPercent = 8;

    [ObservableProperty]
    private bool _ignoreAntialiasing = true;

    /// <summary>Vùng bỏ qua (toạ độ ảnh A), người dùng vẽ trên canvas ở chế độ Khác biệt.</summary>
    public ObservableCollection<SKRectI> IgnoreRects { get; } = [];

    /// <summary>Vùng cần soi (toạ độ ảnh A) khi căn "Soi 1 vùng" - người dùng kéo chuột khoanh trên canvas; null = chưa khoanh.</summary>
    [ObservableProperty]
    private SKRectI? _focusRect;

    // ---- Kết quả Khác biệt ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    private IDiffView? _painter;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _summaryTitle = "Chưa so sánh";

    [ObservableProperty]
    private string _summaryDetail = string.Empty;

    // ---- Tìm ảnh con ----
    /// <summary>Độ khớp tối thiểu 50–100 (%).</summary>
    [ObservableProperty]
    private double _findMinScore = 90;

    [ObservableProperty]
    private TemplateResult? _findResult;

    // ---- Tìm chữ (OCR) ----
    /// <summary>Đọc chữ ảnh A (true) hay B; ô được chọn còn trống thì dùng ảnh ở ô kia.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextTarget))]
    private bool _textUseA = true;

    [ObservableProperty]
    private string _textQuery = string.Empty;

    [ObservableProperty]
    private bool _textMatchCase;

    [ObservableProperty]
    private bool _textMatchDiacritics;

    /// <summary>Chữ đọc được trong <see cref="TextTarget"/>, null khi chưa đọc xong.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOcr))]
    private OcrResult? _ocr;

    /// <summary>Chỗ khớp <see cref="TextQuery"/> trong <see cref="Ocr"/>; null khi ô tìm trống (hiện mọi dòng).</summary>
    [ObservableProperty]
    private List<TextMatch>? _textMatches;

    // ---- So chữ ----
    [ObservableProperty]
    private FormLanguage _textDiffLanguage = FormLanguage.Japanese;

    /// <summary>Hiện cả các chỗ chỉ lệch 1 ký tự (nhiều khả năng OCR đọc lệch) - mặc định ẩn cho đỡ nhiễu.</summary>
    [ObservableProperty]
    private bool _textDiffShowSimilar;

    /// <summary>Ẩn các chỗ OCR đọc mỗi phía 1 kiểu nhưng nét chữ trùng khít (<see cref="TextDiffItem.SameGlyphs"/>). Tắt =
    /// chỉ dựa vào OCR như trước.</summary>
    [ObservableProperty]
    private bool _textDiffHideSameGlyphs = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExportTextDiff))]
    private TextDiffResult? _textDiffResult;

    /// <summary>Mục đang chọn trong danh sách (vẽ khung vàng), 0 = không chọn.</summary>
    [ObservableProperty]
    private int _highlightedItem;

    /// <summary>Danh sách bên phải: vùng khác (Khác biệt) hoặc chỗ tìm thấy (Tìm ảnh con).</summary>
    public ObservableCollection<ResultItem> Items { get; } = [];

    [ObservableProperty]
    private string _itemsTitle = "Các vùng khác";

    public string ImageAText => ImageA?.Describe() ?? "(chưa có ảnh)";
    public string ImageBText => ImageB?.Describe() ?? "(chưa có ảnh)";
    public bool HasBothImages => ImageA is not null && ImageB is not null;
    public bool CanExport => Mode == ViewMode.Diff && Painter is not null;

    /// <summary>Xuất được kết quả So chữ (copy / CSV / HTML): đang ở chế độ So chữ và đã so xong.</summary>
    public bool CanExportTextDiff => Mode == ViewMode.TextDiff && TextDiffResult is not null;

    /// <summary>Tên bộ đọc chữ của lần So chữ gần nhất - ghi vào báo cáo.</summary>
    public string TextDiffReaderName => _textDiffReaderName ?? string.Empty;

    /// <summary>Mục So chữ ứng với 1 dòng của danh sách bên phải (cùng số thứ tự), null nếu không phải chế độ So chữ.</summary>
    public TextDiffItem? TextDiffItemAt(int number) =>
        Mode == ViewMode.TextDiff ? VisibleTextDiffItems.FirstOrDefault(i => i.Number == number) : null;

    /// <summary>Ô sẽ nhận ảnh khi dán / kéo-thả mà không nói rõ ô nào: ô A nếu còn trống, không thì B
    /// (thay ảnh B cũ - kiểu "so ảnh mới nhất với ảnh gốc").</summary>
    public bool NextSlotIsA => ImageA is null;

    /// <summary>Ảnh lớn đang vẽ ở chế độ Tìm ảnh con (ảnh được tìm trong đó).</summary>
    public LoadedImage? FindTarget => FindResult is { Swapped: true } ? ImageB : ImageA;

    /// <summary>Ảnh đang đọc chữ ở chế độ Tìm chữ.</summary>
    public LoadedImage? TextTarget => TextUseA ? ImageA ?? ImageB : ImageB ?? ImageA;

    public bool HasOcr => Ocr is not null;

    public DiffOptions CurrentOptions => new()
    {
        Align = Align,
        ManualOffset = OffsetB,
        ThresholdPercent = ThresholdPercent,
        IgnoreAntialiasing = IgnoreAntialiasing,
        IgnoreRects = IgnoreRects.ToList(),
        FocusRect = FocusRect,
    };

    /// <summary>Các mục So chữ đang hiện (bỏ "gần giống" nếu không bật <see cref="TextDiffShowSimilar"/>, bỏ chỗ nét chữ trùng
    /// nếu bật <see cref="TextDiffHideSameGlyphs"/>) - cùng thứ tự và số như danh sách bên phải, để canvas vẽ đúng các khung đó.</summary>
    public IReadOnlyList<TextDiffItem> VisibleTextDiffItems =>
        TextDiffResult?.Items.Where(i => (TextDiffShowSimilar || i.Kind != TextDiffKind.Similar) && !(TextDiffHideSameGlyphs && i.SameGlyphs))
            .ToList() ?? [];

    public CompareViewModel()
    {
        IgnoreRects.CollectionChanged += (_, _) =>
        {
            RequestCompare();
            if (Mode == ViewMode.TextDiff)
            {
                RequestTextDiff(); // chữ đã đọc nằm trong cache → chỉ ghép lại, nhanh
            }
        };
    }

    /// <summary>Đặt ảnh vào ô A hoặc B. Ảnh cũ KHÔNG Dispose ngay: lượt so sánh nền có thể vẫn đang đọc con trỏ
    /// pixel của nó (huỷ chỉ được kiểm tra định kỳ) → để GC thu hồi khi không còn ai giữ.</summary>
    public void SetImage(bool slotA, LoadedImage image)
    {
        CancelAll();
        ClearResults();
        if (slotA)
        {
            ImageA = image;
            IgnoreRects.Clear(); // vùng bỏ qua vẽ theo ảnh A cũ
            FocusRect = null;
        }
        else
        {
            ImageB = image;
        }
        if (Align == AlignMode.Manual)
        {
            Align = AlignMode.Translate; // ảnh mới → độ lệch chỉnh tay cũ không còn nghĩa
        }
        if (Mode == ViewMode.Text)
        {
            TextUseA = slotA; // Tìm chữ: đọc luôn ảnh vừa đưa vào
        }
        SwipeX = (ImageA?.Bitmap.Width ?? image.Bitmap.Width) / 2.0;
        StatusText = $"Ảnh {(slotA ? "A" : "B")}: {image.Describe()}";
        Refresh();
    }

    /// <summary>Bỏ ảnh ở ô A hoặc B (nút ✕ trên ô). Không Dispose - xem <see cref="SetImage"/>.</summary>
    public void ClearImage(bool slotA)
    {
        CancelAll();
        if (slotA)
        {
            ImageA = null;
            IgnoreRects.Clear();
            FocusRect = null;
        }
        else
        {
            ImageB = null;
        }
        ClearResults(); // sau khi bỏ ảnh: bảng kết quả hiện "Chưa so sánh" thay vì "Đang so sánh…"
        OffsetB = default;
        StatusText = $"Đã bỏ ảnh {(slotA ? "A" : "B")}. Mở / dán / kéo-thả ảnh khác vào ô đó.";
        if (Mode == ViewMode.Text)
        {
            RequestOcr(); // Tìm chữ chỉ cần 1 ảnh → đọc ảnh còn lại
        }
    }

    [RelayCommand]
    private void Swap()
    {
        CancelAll();
        ClearResults();
        (ImageA, ImageB) = (ImageB, ImageA);
        OffsetB = new SKPointI(-OffsetB.X, -OffsetB.Y);
        IgnoreRects.Clear();
        FocusRect = null;
        StatusText = "Đã đổi chỗ ảnh A và B.";
        Refresh();
    }

    /// <summary>Alt + phím mũi tên: dịch ảnh B 1 px (Shift: 10 px) rồi so lại với độ lệch cố định đó.</summary>
    public void Nudge(int dx, int dy)
    {
        if (!HasBothImages || Mode == ViewMode.Find)
        {
            return;
        }
        OffsetB = new SKPointI(OffsetB.X + dx, OffsetB.Y + dy);
        if (Align != AlignMode.Manual)
        {
            Align = AlignMode.Manual; // OnAlignChanged gọi RequestCompare
        }
        else
        {
            RequestCompare();
            if (Mode == ViewMode.TextDiff)
            {
                RequestTextDiff();
            }
        }
    }

    public void AddIgnoreRect(SKRectI rect) => IgnoreRects.Add(rect);

    /// <summary>Bỏ vùng bỏ qua chứa điểm (x, y) (toạ độ ảnh A); true nếu có vùng bị bỏ.</summary>
    public bool RemoveIgnoreRectAt(int x, int y)
    {
        var hit = IgnoreRects.LastOrDefault(r => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom);
        return hit != default && IgnoreRects.Remove(hit);
    }

    [RelayCommand]
    private void ClearIgnoreRects() => IgnoreRects.Clear();

    partial void OnAlignChanged(AlignMode value)
    {
        if (value == AlignMode.Rows)
        {
            IgnoreRects.Clear(); // toạ độ ảnh ghép khác toạ độ ảnh A - không vẽ vùng bỏ qua ở chế độ này
        }
        RequestCompare();
        if (Mode == ViewMode.TextDiff)
        {
            RequestTextDiff(); // chỉnh tay ↔ tự căn: đổi độ lệch dùng để ghép
        }
    }

    partial void OnFocusRectChanged(SKRectI? value)
    {
        if (Align == AlignMode.Focus)
        {
            RequestCompare();
        }
    }

    partial void OnThresholdPercentChanged(double value) => RequestCompare(debounce: true);
    partial void OnIgnoreAntialiasingChanged(bool value) => RequestCompare();
    partial void OnFindMinScoreChanged(double value) => RequestFind(debounce: true);

    partial void OnModeChanged(ViewMode value)
    {
        HighlightedItem = 0;
        if (value == ViewMode.Find && FindResult is null)
        {
            RequestFind();
        }
        if (value == ViewMode.Text && Ocr is null)
        {
            RequestOcr();
        }
        if (value == ViewMode.TextDiff && TextDiffResult is null)
        {
            RequestTextDiff();
        }
        UpdatePanel();
    }

    partial void OnTextDiffLanguageChanged(FormLanguage value)
    {
        TextDiffResult = null;
        if (Mode == ViewMode.TextDiff)
        {
            RequestTextDiff();
        }
        if (Mode == ViewMode.Text)
        {
            RequestOcr(); // Tìm chữ dùng chung ô ngôn ngữ - đọc lại ảnh theo ngôn ngữ mới (có cache thì tức thì)
        }
    }

    partial void OnTextDiffShowSimilarChanged(bool value)
    {
        if (Mode == ViewMode.TextDiff)
        {
            UpdatePanel();
        }
    }

    partial void OnTextDiffHideSameGlyphsChanged(bool value)
    {
        if (Mode == ViewMode.TextDiff)
        {
            UpdatePanel();
        }
    }

    partial void OnTextUseAChanged(bool value)
    {
        if (Mode == ViewMode.Text)
        {
            RequestOcr();
        }
    }

    partial void OnTextQueryChanged(string value) => UpdateTextMatches();
    partial void OnTextMatchCaseChanged(bool value) => UpdateTextMatches();
    partial void OnTextMatchDiacriticsChanged(bool value) => UpdateTextMatches();

    /// <summary>Sau khi đổi ảnh: so sánh lại, và tìm lại nếu đang ở chế độ Tìm ảnh con / Tìm chữ.</summary>
    private void Refresh()
    {
        RequestCompare();
        if (Mode == ViewMode.Find)
        {
            RequestFind();
        }
        if (Mode == ViewMode.Text)
        {
            RequestOcr();
        }
        if (Mode == ViewMode.TextDiff)
        {
            RequestTextDiff();
        }
    }

    /// <summary>So chữ A ↔ B ở nền: đọc chữ 2 ảnh (cache theo ảnh + ngôn ngữ), tự căn (độ lệch chung), rồi ghép đoạn
    /// (Engine/TextDiff). Đọc A chiếm nửa đầu tiến độ, B nửa sau.</summary>
    public async void RequestTextDiff()
    {
        _textDiffCts?.Cancel();
        _textDiffCts = null;
        _textDiffError = null;
        if (ImageA is not { } a || ImageB is not { } b)
        {
            TextDiffResult = null;
            if (Mode == ViewMode.TextDiff)
            {
                UpdatePanel();
            }
            return;
        }
        var cts = _textDiffCts = new CancellationTokenSource();
        var language = TextDiffLanguage;
        var ignore = IgnoreRects.ToList();
        var manualOffset = Align == AlignMode.Manual ? OffsetB : (SKPointI?)null;
        IsBusy = true;
        TextDiffResult = null;
        if (Mode == ViewMode.TextDiff)
        {
            UpdatePanel();
        }
        var progress = new Progress<double>(p =>
        {
            if (ReferenceEquals(_textDiffCts, cts) && Mode == ViewMode.TextDiff)
            {
                SummaryTitle = p < 0.6 ? $"Đang đọc chữ… {p * 100:0}%" : $"Đang kiểm tra lại từng chỗ… {p * 100:0}%";
            }
        });
        IProgress<double> report = progress;
        try
        {
            var reader = FormReaders.Create(language, out var note);
            _textDiffReaderName = reader.Name;
            _textDiffReaderNote = note;
            var cachedA = CachedFormOcr(a, language);
            var cachedB = CachedFormOcr(b, language);
            var verifyReaders = FormReaders.VerifyReaders(reader, language);
            var result = await Task.Run(() =>
            {
                var ocrA = cachedA ?? reader.Read(a.Bitmap, cts.Token, new SyncProgress(p => report.Report(p * 0.3)));
                var ocrB = cachedB ?? reader.Read(b.Bitmap, cts.Token, new SyncProgress(p => report.Report(0.3 + p * 0.3)));
                var offset = manualOffset ?? Aligner.FindOffset(a.Bitmap, b.Bitmap, cts.Token);
                var diff = TextDiff.Compare(ocrA, ocrB, offset, ignore, a.Bitmap, b.Bitmap, cts.Token);
                // Đọc lại riêng từng chỗ nghi khác: bỏ chỗ thực ra giống (OCR cả trang đọc sai 1 phía), thay chữ rác.
                diff = TextDiffVerifier.Verify(diff, a.Bitmap, b.Bitmap, verifyReaders, cts.Token,
                    new SyncProgress(p => report.Report(0.6 + p * 0.4)));
                return (ocrA, ocrB, diff);
            }, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }
            StoreFormOcr(a, language, result.ocrA);
            StoreFormOcr(b, language, result.ocrB);
            TextDiffResult = result.diff;
            if (Mode == ViewMode.TextDiff)
            {
                UpdatePanel();
            }
            var d = result.diff;
            Log.LogInformation("So chữ {A} ↔ {B} ({Reader}): {Items} chỗ khác (đổi {Changed}, chỉ A {OnlyA}, chỉ B {OnlyB}, màu {Color}, gần giống {Similar}), giống {Same}, đoạn A {SegA} / B {SegB}, đọc A {MsA} ms, B {MsB} ms",
                a.Name, b.Name, reader.Name, d.Items.Count, d.Count(TextDiffKind.Changed), d.Count(TextDiffKind.OnlyInA),
                d.Count(TextDiffKind.OnlyInB), d.Count(TextDiffKind.ColorChanged), d.Count(TextDiffKind.Similar), d.SameCount,
                d.SegmentsA, d.SegmentsB, (int)result.ocrA.Elapsed.TotalMilliseconds, (int)result.ocrB.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            var error = ex is AggregateException { InnerException: { } inner } ? inner : ex;
            _textDiffError = error is DllNotFoundException or BadImageFormatException or TypeInitializationException
                ? $"Thiếu thư viện nhận dạng chữ cho máy này: {error.Message}"
                : error.Message;
            if (Mode == ViewMode.TextDiff)
            {
                UpdatePanel();
            }
            Log.LogError(ex, "So chữ thất bại");
        }
        finally
        {
            if (ReferenceEquals(_textDiffCts, cts))
            {
                IsBusy = false;
            }
        }
    }

    private OcrResult? CachedFormOcr(LoadedImage image, FormLanguage language) =>
        _formOcrCache.TryGetValue(image, out var byLanguage) && byLanguage.TryGetValue(language, out var ocr) ? ocr : null;

    private void StoreFormOcr(LoadedImage image, FormLanguage language, OcrResult ocr) =>
        _formOcrCache.GetOrCreateValue(image)[language] = ocr;

    /// <summary>IProgress gọi thẳng (không qua SynchronizationContext) - báo tiến độ từ luồng nền sang Progress của UI.</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    /// <summary>Đọc chữ ảnh <see cref="TextTarget"/> ở nền (lấy từ cache nếu đã đọc), rồi tìm lại chữ đang gõ.</summary>
    public async void RequestOcr()
    {
        _ocrCts?.Cancel();
        _ocrCts = null;
        Ocr = null;
        _ocrError = null;
        if (TextTarget is not { } target)
        {
            UpdateTextMatches();
            return;
        }
        // Tiếng Nhật: cùng bộ đọc form của So chữ (Windows OCR "ja", dự phòng Tesseract "jpn") và cùng cache - đã So chữ
        // rồi thì Tìm chữ tức thì. Tiếng Việt / Anh: Tesseract "vie" như trước. (Bản cũ luôn đọc "vie" → chữ Nhật ra rác,
        // tìm 確定状況 không thấy.)
        var language = TextDiffLanguage;
        var cached = language == FormLanguage.Japanese
            ? CachedFormOcr(target, language)
            : _ocrCache.TryGetValue(target, out var vie) ? vie : null;
        if (cached is not null)
        {
            _textReaderName = language == FormLanguage.Japanese ? _textReaderName ?? _textDiffReaderName : "Tesseract (tiếng Việt / Anh)";
            Ocr = cached;
            UpdateTextMatches();
            return;
        }
        var cts = _ocrCts = new CancellationTokenSource();
        IsBusy = true;
        UpdateTextMatches();
        // Progress tạo trên luồng UI → Report từ luồng nền được đưa về luồng UI.
        var progress = new Progress<double>(p =>
        {
            if (ReferenceEquals(_ocrCts, cts) && Mode == ViewMode.Text)
            {
                SummaryTitle = $"Đang đọc chữ… {p * 100:0}%";
            }
        });
        try
        {
            IProgress<double> report = progress;
            OcrResult result;
            if (language == FormLanguage.Japanese)
            {
                var reader = FormReaders.Create(language, out var note);
                _textReaderName = reader.Name;
                _textReaderNote = note;
                result = await Task.Run(() => reader.Read(target.Bitmap, cts.Token, new SyncProgress(report.Report)), cts.Token);
            }
            else
            {
                _textReaderName = "Tesseract (tiếng Việt / Anh)";
                _textReaderNote = null;
                result = await Task.Run(() => TextRecognizer.Recognize(target.Bitmap, cts.Token, progress), cts.Token);
            }
            if (cts.IsCancellationRequested)
            {
                return;
            }
            if (language == FormLanguage.Japanese)
            {
                StoreFormOcr(target, language, result);
            }
            else
            {
                _ocrCache.AddOrUpdate(target, result);
            }
            Ocr = result;
            UpdateTextMatches();
            Log.LogInformation("Đọc chữ {Name} ({W}x{H}): {Lines} dòng, {Ms} ms",
                target.Name, target.Bitmap.Width, target.Bitmap.Height, result.Lines.Count, (int)result.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            var error = ex is AggregateException { InnerException: { } inner } ? inner : ex; // lỗi từ Parallel.For
            _ocrError = error is DllNotFoundException or BadImageFormatException or TypeInitializationException
                ? $"Thiếu thư viện nhận dạng chữ (Tesseract) cho máy này: {error.Message}"
                : error.Message;
            UpdateTextMatches();
            Log.LogError(ex, "Đọc chữ thất bại");
        }
        finally
        {
            if (ReferenceEquals(_ocrCts, cts))
            {
                IsBusy = false;
            }
        }
    }

    /// <summary>Tìm lại <see cref="TextQuery"/> (nhanh, chạy luôn trên luồng UI) rồi cập nhật bảng bên phải.</summary>
    private void UpdateTextMatches()
    {
        TextMatches = Ocr is { } ocr && !string.IsNullOrWhiteSpace(TextQuery)
            ? TextSearch.Find(ocr, TextQuery, TextMatchCase, TextMatchDiacritics)
            : null;
        if (Mode == ViewMode.Text)
        {
            UpdatePanel();
        }
    }

    /// <summary>So sánh lại ở nền; <paramref name="debounce"/> khi kéo thanh ngưỡng để không so liên tục.</summary>
    public async void RequestCompare(bool debounce = false)
    {
        _compareCts?.Cancel();
        if (ImageA is not { } a || ImageB is not { } b)
        {
            return;
        }
        var cts = _compareCts = new CancellationTokenSource();
        var options = CurrentOptions;
        IsBusy = true;
        if (Mode == ViewMode.Diff)
        {
            SummaryTitle = "Đang so sánh…";
        }
        try
        {
            if (debounce)
            {
                await Task.Delay(200, cts.Token);
            }
            var view = await Task.Run(() => ImageComparer.CreateView(a.Bitmap, b.Bitmap, options, cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
            {
                view.Dispose();
                return;
            }
            // Không Dispose kết quả cũ: xuất báo cáo chạy nền có thể vẫn đang đọc nó - để GC thu hồi.
            Painter = view;
            if (view.Stats.Align != AlignMode.Rows)
            {
                OffsetB = view.Stats.OffsetB;
            }
            UpdatePanel();
            var s = view.Stats;
            Log.LogInformation("So sánh {A} ↔ {B}: {Regions} vùng, {Identical:0.###}% giống, SSIM {Ssim:0.####}, {Note}, {Ms} ms",
                a.Name, b.Name, s.RegionCount, s.IdenticalPercent, s.Ssim, s.AlignNote, (int)s.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            // Có lượt so sánh mới thay thế.
        }
        catch (Exception ex)
        {
            SummaryTitle = "So sánh thất bại";
            SummaryDetail = ex.Message;
            Log.LogError(ex, "So sánh thất bại");
        }
        finally
        {
            if (ReferenceEquals(_compareCts, cts))
            {
                IsBusy = false;
            }
        }
    }

    public async void RequestFind(bool debounce = false)
    {
        _findCts?.Cancel();
        if (ImageA is not { } a || ImageB is not { } b)
        {
            return;
        }
        var cts = _findCts = new CancellationTokenSource();
        double minScore = FindMinScore / 100;
        IsBusy = true;
        if (Mode == ViewMode.Find)
        {
            SummaryTitle = "Đang tìm…";
        }
        try
        {
            if (debounce)
            {
                await Task.Delay(250, cts.Token);
            }
            var result = await Task.Run(() => TemplateMatcher.Find(a.Bitmap, b.Bitmap, minScore, cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }
            FindResult = result;
            UpdatePanel();
            Log.LogInformation("Tìm ảnh con {Small} trong {Big}: {Count} chỗ (≥ {Min:0.##}), tốt nhất {Best:0.###}, {Ms} ms",
                result.Swapped ? a.Name : b.Name, result.Swapped ? b.Name : a.Name, result.Matches.Count, minScore, result.BestScore,
                (int)result.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SummaryTitle = "Tìm thất bại";
            SummaryDetail = ex.Message;
            Log.LogError(ex, "Tìm ảnh con thất bại");
        }
        finally
        {
            if (ReferenceEquals(_findCts, cts))
            {
                IsBusy = false;
            }
        }
    }

    /// <summary>Tiêu đề, mô tả và danh sách bên phải theo chế độ đang xem.</summary>
    private void UpdatePanel()
    {
        Items.Clear();
        HighlightedItem = 0;
        ItemsTitle = Mode switch
        {
            ViewMode.Find => "Các chỗ tìm thấy (khớp nhất trước)",
            ViewMode.Text => TextMatches is null ? "Các dòng chữ đọc được" : "Các chỗ tìm thấy",
            ViewMode.TextDiff => "Các chỗ khác về chữ",
            _ => "Các vùng khác",
        };
        if (Mode == ViewMode.Find)
        {
            ShowFindPanel();
        }
        else if (Mode == ViewMode.Text)
        {
            ShowTextPanel();
        }
        else if (Mode == ViewMode.TextDiff)
        {
            ShowTextDiffPanel();
        }
        else
        {
            ShowDiffPanel();
        }
    }

    private void ShowDiffPanel()
    {
        if (Painter is not { } view)
        {
            SummaryTitle = HasBothImages ? "Đang so sánh…" : "Chưa so sánh";
            SummaryDetail = string.Empty;
            return;
        }
        var s = view.Stats;
        foreach (var region in view.Regions)
        {
            var b = region.Bounds;
            string text = region.Kind == RegionKind.Changed
                ? $"({b.Left}, {b.Top})  {b.Width} × {b.Height}"
                : $"{(region.Kind == RegionKind.OnlyInB ? "thêm" : "bỏ")} {b.Height} dòng (y {b.Top})";
            string color = region.Kind switch { RegionKind.OnlyInA => "#2B7BD6", RegionKind.OnlyInB => "#D86445", _ => "#E51A1A" };
            Items.Add(new ResultItem(region.Number, text, b, color));
        }

        SummaryTitle = s.IsIdentical ? "Giống hệt nhau"
            : s.RegionCount > 0 ? $"{s.RegionCount} chỗ khác nhau"
            : "Phần chồng nhau giống hệt";
        var lines = new List<string> { $"Pixel giống: {s.IdenticalPercent.ToString("0.###", Vi)}%" };
        if (!double.IsNaN(s.Ssim))
        {
            lines.Add($"SSIM: {s.Ssim.ToString("0.####", Vi)} (1 = giống hệt)");
        }
        lines.Add(s.AlignNote);
        if (SmallShiftHint(s) is { } shiftHint)
        {
            lines.Add(shiftHint);
        }
        if (Align == AlignMode.Focus && FocusRect is null)
        {
            lines.Insert(0, "Kéo chuột trái trên ảnh để khoanh vùng cần soi - đang so cả trang như Tự căn chỉnh.");
        }
        if (s.Align != AlignMode.Rows && (s.OnlyInA > 0 || s.OnlyInB > 0))
        {
            lines.Add($"Chỉ có ở A: {s.OnlyInA.ToString("N0", Vi)} px · chỉ ở B: {s.OnlyInB.ToString("N0", Vi)} px (sọc)");
        }
        if (IgnoreRects.Count > 0)
        {
            lines.Add($"Bỏ qua {IgnoreRects.Count} vùng (khung xám)");
        }
        if ((IsJpeg(ImageA) || IsJpeg(ImageB)) && s.RegionCount > 0 && s.Ssim > 0.98 && ThresholdPercent < 15)
        {
            lines.Add("Ảnh JPG: nhiễu nén hay bị tính là khác - thử tăng ngưỡng lên ~20%.");
        }
        if (LayoutShiftWarning(view) is { } warning)
        {
            lines.Add(warning);
        }
        lines.Add($"{(int)s.Elapsed.TotalMilliseconds} ms");
        SummaryDetail = string.Join(Environment.NewLine, lines);
        StatusText = Align == AlignMode.Focus
            ? $"{SummaryTitle}. Kéo chuột trái để khoanh vùng khác; chuột phải / giữa để cuộn ảnh."
            : $"{SummaryTitle}. Bấm 1 vùng trong danh sách bên phải để phóng tới vùng đó.";
    }

    private void ShowFindPanel()
    {
        if (FindResult is not { } r || ImageA is null || ImageB is null)
        {
            SummaryTitle = HasBothImages ? "Đang tìm…" : "Chưa tìm";
            SummaryDetail = "Tìm ảnh nhỏ hơn trong ảnh lớn hơn (vd 1 nút, 1 icon cắt ra).";
            return;
        }
        foreach (var m in r.Matches)
        {
            Items.Add(new ResultItem(m.Number, $"({m.Bounds.Left}, {m.Bounds.Top})  khớp {(m.Score * 100).ToString("0.#", Vi)}%", m.Bounds, "#1A7F37"));
        }
        var (small, big) = r.Swapped ? (ImageA, ImageB) : (ImageB, ImageA);
        SummaryTitle = r.Matches.Count switch
        {
            0 => "Không tìm thấy",
            1 => "Tìm thấy 1 chỗ",
            _ => $"Tìm thấy {r.Matches.Count} chỗ",
        };
        var lines = new List<string>
        {
            $"Tìm {small.Name} ({small.Bitmap.Width} × {small.Bitmap.Height}) trong {big.Name}",
        };
        if (r.Matches.Count == 0)
        {
            lines.Add($"Chỗ giống nhất chỉ khớp {(r.BestScore * 100).ToString("0.#", Vi)}% - thử giảm độ khớp tối thiểu.");
        }
        else if (r.Matches.Count >= 50)
        {
            lines.Add("Nhiều chỗ na ná nhau - chỉ hiện 50 chỗ khớp nhất; tăng độ khớp tối thiểu để lọc.");
        }
        lines.Add($"{(int)r.Elapsed.TotalMilliseconds} ms");
        SummaryDetail = string.Join(Environment.NewLine, lines);
        StatusText = $"{SummaryTitle}. Bấm 1 kết quả để phóng tới.";
    }

    private void ShowTextPanel()
    {
        var target = TextTarget;
        if (Ocr is not { } ocr || target is null)
        {
            if (_ocrError is not null && target is not null)
            {
                SummaryTitle = "Không đọc được chữ";
                SummaryDetail = _ocrError;
                return;
            }
            SummaryTitle = target is null ? "Chưa có ảnh" : "Đang đọc chữ…";
            SummaryDetail = target is null
                ? "Mở / dán / kéo-thả 1 ảnh để đọc và tìm chữ trong đó."
                : $"{target.Name} - ảnh dài có thể mất vài giây.";
            return;
        }
        string slot = ReferenceEquals(target, ImageA) ? "A" : "B";
        if (TextMatches is not { } matches)
        {
            // Chưa gõ gì: liệt kê các dòng đọc được (bấm → phóng tới dòng đó).
            for (int i = 0; i < ocr.Lines.Count; i++)
            {
                Items.Add(new ResultItem(i + 1, ocr.Lines[i].Text, ocr.Lines[i].Bounds, "#6E7781"));
            }
            SummaryTitle = ocr.Lines.Count == 0 ? "Không thấy chữ nào" : $"Đọc được {ocr.Lines.Count} dòng";
            SummaryDetail = $"Ảnh {slot}: {target.Name}{Environment.NewLine}Gõ vào ô Tìm để khoanh chỗ có chữ đó.{Environment.NewLine}{ReaderLine()} · {(int)ocr.Elapsed.TotalMilliseconds} ms"
                + (_textReaderNote is null ? string.Empty : Environment.NewLine + _textReaderNote);
            StatusText = ocr.Lines.Count == 0 ? "Không đọc được chữ nào trong ảnh." : $"{SummaryTitle} trong ảnh {slot}.";
            return;
        }
        bool approximate = matches.Count > 0 && matches[0].Approximate;
        foreach (var m in matches)
        {
            Items.Add(new ResultItem(m.Number, (m.Approximate ? "≈ " : string.Empty) + m.LineText, m.Bounds, m.Approximate ? "#8250DF" : "#BF8700"));
        }
        SummaryTitle = matches.Count switch
        {
            0 => $"Không thấy \"{TextQuery.Trim()}\"",
            _ when approximate => $"Không thấy chính xác - {matches.Count} chỗ gần đúng",
            1 => "Tìm thấy 1 chỗ",
            _ => $"Tìm thấy {matches.Count} chỗ",
        };
        var lines = new List<string> { $"Ảnh {slot}: {target.Name}" };
        if (approximate)
        {
            lines.Add("Chữ đọc từ ảnh sai 1–2 ký tự so với chữ cần tìm (≈) - soi lại trên ảnh.");
        }
        if (matches.Count == 0)
        {
            lines.Add(TextMatchDiacritics || TextMatchCase
                ? "Thử bỏ \"Phân biệt dấu\" / \"Phân biệt hoa thường\" - chữ đọc từ ảnh có thể sai dấu."
                : "Chữ đọc từ ảnh có thể sai vài ký tự - thử tìm 1 đoạn ngắn hơn.");
            lines.Add(ReaderLine() + (TextDiffLanguage == FormLanguage.VietnameseEnglish ? " - chữ Nhật thì chọn \"Tiếng Nhật\" ở ô ngôn ngữ." : "."));
        }
        SummaryDetail = string.Join(Environment.NewLine, lines);
        StatusText = $"{SummaryTitle}. Bấm 1 kết quả để phóng tới.";
    }

    /// <summary>"Đọc bằng …" - người dùng thấy ngay đang đọc theo tiếng gì (chọn nhầm ngôn ngữ = toàn chữ rác).</summary>
    private string ReaderLine() =>
        $"Đọc bằng {_textReaderName ?? (TextDiffLanguage == FormLanguage.Japanese ? "OCR tiếng Nhật" : "OCR tiếng Việt / Anh")}";

    /// <summary>1 vùng khác phủ ≥ 60% ảnh: 2 ảnh lệch bố cục cục bộ (khác font / trình duyệt...) mà tự căn 1 độ lệch
    /// chung không bù được → so pixel tô đỏ gần hết, không còn chỉ ra chỗ khác thật. Chỉ thêm lời nhắc, không đổi
    /// kết quả so.</summary>
    /// <summary>Tự căn ra độ lệch chỉ vài px: thường cả trang dịch đều do vùng trang của 2 trình duyệt bắt đầu lệch nhau
    /// (IE mode có viền lõm 2px quanh trang - document.documentElement.clientLeft / clientTop = 2), không phải bố cục lệch:
    /// số đo DOM vẫn trùng. Báo để người xem không hiểu nhầm (gặp 2026-10-07: IE mode ↔ Edge lệch (2, 2)).</summary>
    private static string? SmallShiftHint(DiffStats s)
    {
        const int MaxSmallShift = 4;
        if (s.Align != AlignMode.Translate || s.OffsetB == SKPointI.Empty
            || Math.Abs(s.OffsetB.X) > MaxSmallShift || Math.Abs(s.OffsetB.Y) > MaxSmallShift)
        {
            return null;
        }
        return $"ℹ Cả trang B lệch ({s.OffsetB.X}, {s.OffsetB.Y}) px so với A: phần lệch đều này thường do vùng trang của 2 trình duyệt " +
            "bắt đầu lệch nhau (vd IE mode có viền 2px quanh trang) hoặc chụp lệch vài px - số đo DOM vẫn trùng - và đã được tự căn bù. " +
            "Chỗ còn khác sau khi căn mới là khác thật; khối nào lệch riêng thì khoanh bằng \"Soi 1 vùng\".";
    }

    private string? LayoutShiftWarning(IDiffView view)
    {
        if (view.Stats.Align == AlignMode.Rows || view.Regions.Count == 0 || ImageA is not { } a)
        {
            return null;
        }
        double area = (double)a.Bitmap.Width * a.Bitmap.Height;
        double largest = view.Regions.Max(r => (double)r.Bounds.Width * r.Bounds.Height);
        double share = largest / area;
        return share >= 0.6
            ? $"⚠ 1 vùng phủ {(share * 100).ToString("0", Vi)}% ảnh: 2 ảnh lệch bố cục (khác font / trình duyệt?) nên so pixel tô gần hết. Thử chế độ \"So chữ\" để xem chữ / giá trị nào khác, hoặc căn \"Soi 1 vùng\" rồi khoanh từng chỗ cần xem."
            : null;
    }

    private void ShowTextDiffPanel()
    {
        if (!HasBothImages)
        {
            SummaryTitle = "Chưa so chữ";
            SummaryDetail = "Đưa đủ ảnh A và B (2 ảnh chụp cùng 1 màn hình) để so chữ / giá trị giữa 2 ảnh.";
            return;
        }
        if (TextDiffResult is not { } r)
        {
            SummaryTitle = _textDiffError is null ? "Đang đọc chữ…" : "Không so được chữ";
            SummaryDetail = _textDiffError ?? $"{_textDiffReaderName ?? "Đang chuẩn bị"} - đọc chữ cả 2 ảnh, mỗi ảnh vài giây.";
            return;
        }
        var visible = VisibleTextDiffItems;
        foreach (var item in visible)
        {
            Items.Add(new ResultItem(item.Number, DescribeTextDiff(item), item.BoundsInA(r.OffsetB), TextDiffColor(item.Kind)));
        }
        // Đếm theo mục đang hiện (đã ẩn chỗ nét chữ trùng nếu bật) - khớp với tiêu đề và danh sách.
        int VisibleCount(TextDiffKind kind) => visible.Count(i => i.Kind == kind);
        int changed = VisibleCount(TextDiffKind.Changed), onlyA = VisibleCount(TextDiffKind.OnlyInA), onlyB = VisibleCount(TextDiffKind.OnlyInB);
        int color = VisibleCount(TextDiffKind.ColorChanged), similar = r.Count(TextDiffKind.Similar);
        int shown = visible.Count;
        SummaryTitle = shown == 0 ? (similar > 0 ? "Chữ giống nhau (trừ vài ký tự nghi do OCR)" : "Chữ giống nhau") : $"{shown} chỗ khác về chữ";
        var lines = new List<string>
        {
            $"Đổi chữ: {changed} · Chỉ ở A: {onlyA} · Chỉ ở B: {onlyB} · Khác màu chữ: {color}",
            $"Giống: {r.SameCount} đoạn (đọc được A: {r.SegmentsA}, B: {r.SegmentsB}), trong đó {r.VerifiedSame} chỗ nghi khác đã đọc lại riêng → giống",
        };
        if (similar > 0)
        {
            lines.Add(TextDiffShowSimilar
                ? $"Đang hiện {similar} chỗ gần giống (nghi OCR đọc lệch)."
                : $"Ẩn {similar} chỗ gần giống (nghi OCR đọc lệch).");
        }
        int sameGlyphs = r.Items.Count(i => i.SameGlyphs && (TextDiffShowSimilar || i.Kind != TextDiffKind.Similar));
        if (sameGlyphs > 0)
        {
            lines.Add(TextDiffHideSameGlyphs
                ? $"Ẩn {sameGlyphs} chỗ OCR đọc mỗi phía 1 kiểu nhưng nét chữ trùng khít (chữ xám ô bị khoá…) - tắt \"So nét chữ\" để xem."
                : $"Có {sameGlyphs} chỗ (≡) nét chữ trùng khít - nhiều khả năng OCR đọc sai, không phải khác thật.");
        }
        if (IgnoreRects.Count > 0)
        {
            lines.Add($"Bỏ qua chữ trong {IgnoreRects.Count} vùng bỏ qua (vẽ ở chế độ Khác biệt)");
        }
        lines.Add($"Đọc bằng {_textDiffReaderName} - chữ đọc từ ảnh có thể sai, soi lại trên ảnh.");
        if (_textDiffReaderNote is not null)
        {
            lines.Add(_textDiffReaderNote);
        }
        SummaryDetail = string.Join(Environment.NewLine, lines);
        StatusText = $"{SummaryTitle}. Bấm 1 mục bên phải để phóng tới chỗ đó trên cả 2 ảnh.";
    }

    private static string DescribeTextDiff(TextDiffItem item) => (item.SameGlyphs ? "≡ " : string.Empty) + item.Kind switch
    {
        TextDiffKind.Changed => $"「{item.A!.Text}」→「{item.B!.Text}」",
        TextDiffKind.Similar => $"≈ 「{item.A!.Text}」→「{item.B!.Text}」",
        TextDiffKind.OnlyInA => $"chỉ A: 「{item.A!.Text}」",
        TextDiffKind.OnlyInB => $"chỉ B: 「{item.B!.Text}」",
        TextDiffKind.ColorChanged => $"màu 「{item.A!.Text}」 {item.Note}",
        _ => item.A?.Text ?? item.B?.Text ?? string.Empty,
    };

    /// <summary>Màu theo loại: đổi chữ đỏ, chỉ A xanh (màu ảnh A), chỉ B cam (màu ảnh B), khác màu tím, gần giống xám.</summary>
    public static string TextDiffColor(TextDiffKind kind) => TextDiffReport.KindColor(kind);

    private static bool IsJpeg(LoadedImage? image) =>
        image?.Path is { } p && (p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));

    private void ClearResults()
    {
        Painter = null; // không Dispose - xem RequestCompare
        FindResult = null;
        Ocr = null;
        TextMatches = null;
        TextDiffResult = null;
        UpdatePanel();
    }

    private void CancelAll()
    {
        _textDiffCts?.Cancel();
        _textDiffCts = null;
        _compareCts?.Cancel();
        _compareCts = null;
        _findCts?.Cancel();
        _findCts = null;
        _ocrCts?.Cancel();
        _ocrCts = null;
    }
}
