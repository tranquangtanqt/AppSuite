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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImageAText), nameof(HasBothImages), nameof(TextTarget))]
    private LoadedImage? _imageA;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImageBText), nameof(HasBothImages), nameof(TextTarget))]
    private LoadedImage? _imageB;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
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
    };

    public CompareViewModel()
    {
        IgnoreRects.CollectionChanged += (_, _) => RequestCompare();
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
        UpdatePanel();
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
        if (_ocrCache.TryGetValue(target, out var cached))
        {
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
            var result = await Task.Run(() => TextRecognizer.Recognize(target.Bitmap, cts.Token, progress), cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }
            _ocrCache.AddOrUpdate(target, result);
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
        lines.Add($"{(int)s.Elapsed.TotalMilliseconds} ms");
        SummaryDetail = string.Join(Environment.NewLine, lines);
        StatusText = $"{SummaryTitle}. Bấm 1 vùng trong danh sách bên phải để phóng tới vùng đó.";
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
            SummaryDetail = $"Ảnh {slot}: {target.Name}{Environment.NewLine}Gõ vào ô Tìm để khoanh chỗ có chữ đó.{Environment.NewLine}{(int)ocr.Elapsed.TotalMilliseconds} ms";
            StatusText = ocr.Lines.Count == 0 ? "Không đọc được chữ nào trong ảnh." : $"{SummaryTitle} trong ảnh {slot}.";
            return;
        }
        foreach (var m in matches)
        {
            Items.Add(new ResultItem(m.Number, m.LineText, m.Bounds, "#BF8700"));
        }
        SummaryTitle = matches.Count switch
        {
            0 => $"Không thấy \"{TextQuery.Trim()}\"",
            1 => "Tìm thấy 1 chỗ",
            _ => $"Tìm thấy {matches.Count} chỗ",
        };
        var lines = new List<string> { $"Ảnh {slot}: {target.Name}" };
        if (matches.Count == 0)
        {
            lines.Add(TextMatchDiacritics || TextMatchCase
                ? "Thử bỏ \"Phân biệt dấu\" / \"Phân biệt hoa thường\" - chữ đọc từ ảnh có thể sai dấu."
                : "Chữ đọc từ ảnh có thể sai vài ký tự - thử tìm 1 đoạn ngắn hơn.");
        }
        SummaryDetail = string.Join(Environment.NewLine, lines);
        StatusText = $"{SummaryTitle}. Bấm 1 kết quả để phóng tới.";
    }

    private static bool IsJpeg(LoadedImage? image) =>
        image?.Path is { } p && (p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));

    private void ClearResults()
    {
        Painter = null; // không Dispose - xem RequestCompare
        FindResult = null;
        Ocr = null;
        TextMatches = null;
        UpdatePanel();
    }

    private void CancelAll()
    {
        _compareCts?.Cancel();
        _compareCts = null;
        _findCts?.Cancel();
        _findCts = null;
        _ocrCts?.Cancel();
        _ocrCts = null;
    }
}
