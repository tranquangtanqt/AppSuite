using System.Diagnostics;
using System.Runtime.CompilerServices;
using Common.Ocr;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ScreenCapture.ViewModels;
using SkiaSharp;

namespace ScreenCapture.Views;

/// <summary>Tìm chữ trong ảnh (khung bên phải, Ctrl+F) + Copy chữ trong vùng chọn - đọc chữ bằng Common.Ocr (Windows OCR
/// tiếng Nhật / Tesseract tiếng Việt - Anh), cùng cách tìm với ImageCompare (không phân biệt dấu / hoa thường, gần đúng ≈).
/// Đọc ảnh nền của tab (không gồm hình đã vẽ) 1 lần, nhớ theo tab + ảnh + ngôn ngữ; ảnh đổi (cắt, xoay, hiệu ứng, Undo...)
/// thì đọc lại khi khung đang mở. Chỗ khớp vẽ đè lên canvas (<see cref="DrawTextSearchOverlay"/>), không vẽ vào ảnh - muốn
/// giữ lại thì "Tô Highlight" thành hình Highlight thật.</summary>
public sealed partial class EditorWindow
{
    /// <summary>Kết quả đọc chữ của 1 tab: đúng với <paramref name="Bitmap"/> (ảnh đổi = đọc lại) và ngôn ngữ đã chọn.</summary>
    private sealed record OcrEntry(SKBitmap Bitmap, FormLanguage Language, OcrResult Result, string Reader);

    private readonly ConditionalWeakTable<EditorViewModel, OcrEntry> _ocrCache = new();
    /// <summary>Bộ đọc theo ngôn ngữ, tạo 1 lần (Tesseract giữ pool engine, Windows OCR tạo engine theo ngôn ngữ).</summary>
    private readonly Dictionary<FormLanguage, (IFormTextReader Reader, string? Note)> _ocrReaders = [];
    private FormLanguage _ocrLanguage = FormLanguage.Japanese;
    private CancellationTokenSource? _ocrCts;
    private int _textSearchVersion;
    private bool _syncingOcrLanguage;

    /// <summary>Chỗ khớp đang tô trên canvas (toạ độ ảnh) và mục đang chọn trong danh sách (khung đậm).</summary>
    private List<TextMatch> _textMatches = [];
    private SKRect? _textFocus;

    /// <summary>Ngôn ngữ đọc chữ - launcher gán theo cài đặt lúc mở Editor, đổi ở ô ngôn ngữ thì báo
    /// <see cref="OcrLanguageChanged"/> để lưu lại.</summary>
    public FormLanguage OcrLanguage
    {
        get => _ocrLanguage;
        set
        {
            _ocrLanguage = value;
            _syncingOcrLanguage = true;
            OcrLanguageBox.SelectedItem = OcrLanguageBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == value.ToString());
            _syncingOcrLanguage = false;
        }
    }

    public event EventHandler<FormLanguage>? OcrLanguageChanged;

    private bool TextSearchOpen => TextSearchPanel.Visibility == Visibility.Visible;

    private void InitTextSearch() => OcrLanguage = _ocrLanguage;

    // ---- Mở / đóng khung ----

    private void TextSearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (TextSearchButton.IsChecked == true)
        {
            OpenTextSearch();
        }
        else
        {
            CloseTextSearch();
        }
    }

    private void TextSearchCloseButton_Click(object sender, RoutedEventArgs e) => CloseTextSearch();

    /// <summary>Mở khung Tìm chữ (nút Tìm chữ / Ctrl+F; đang mở thì chỉ đưa con trỏ về ô tìm) và đọc chữ ảnh đang mở nếu chưa đọc.</summary>
    private void OpenTextSearch()
    {
        TextSearchPanel.Visibility = Visibility.Visible;
        TextSearchButton.IsChecked = true;
        TextSearchBox.Focus(FocusState.Programmatic);
        TextSearchBox.SelectAll();
        _ = RefreshTextSearchAsync();
    }

    private void CloseTextSearch()
    {
        _ocrCts?.Cancel();
        TextSearchPanel.Visibility = Visibility.Collapsed;
        TextSearchButton.IsChecked = false;
        ClearTextHits();
        CanvasScroller.Focus(FocusState.Programmatic); // phím tắt của Editor chạy lại
    }

    private void ClearTextHits()
    {
        _textMatches = [];
        _textFocus = null;
        Canvas.Invalidate();
    }

    /// <summary>Focus đang ở trong khung Tìm chữ (ô tìm, ô ngôn ngữ, danh sách) → phím tắt của Editor không chạy (gõ Backspace
    /// trong ô tìm không được xoá hình đang chọn, Enter không được Cắt ảnh theo vùng chọn...).</summary>
    private bool FocusInTextSearch()
    {
        if (!TextSearchOpen || Content.XamlRoot is null)
        {
            return false;
        }
        for (var node = FocusManager.GetFocusedElement(Content.XamlRoot) as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, TextSearchPanel))
            {
                return true;
            }
        }
        return false;
    }

    // ---- Đọc chữ ----

    private (IFormTextReader Reader, string? Note) GetReader(FormLanguage language)
    {
        if (!_ocrReaders.TryGetValue(language, out var entry))
        {
            var reader = FormReaders.CreateForSearch(language, out var note);
            entry = (reader, note);
            _ocrReaders[language] = entry;
        }
        return entry;
    }

    /// <summary>Kết quả đọc chữ còn đúng với ảnh + ngôn ngữ hiện tại của tab, null nếu chưa đọc / ảnh đã đổi.</summary>
    private OcrEntry? CachedOcr(EditorViewModel vm) =>
        _ocrCache.TryGetValue(vm, out var entry) && ReferenceEquals(entry.Bitmap, vm.Bitmap) && entry.Language == _ocrLanguage ? entry : null;

    /// <summary>Đọc chữ ảnh nền của tab (chạy nền, báo % ở khung Tìm chữ). Null nếu bị huỷ (đóng khung, đổi tab / ảnh / ngôn ngữ).</summary>
    private async Task<OcrEntry?> EnsureOcrAsync(EditorViewModel vm)
    {
        if (CachedOcr(vm) is { } cached)
        {
            return cached;
        }
        _ocrCts?.Cancel();
        var cts = _ocrCts = new CancellationTokenSource();
        var language = _ocrLanguage;
        var bitmap = vm.Bitmap;
        var (reader, note) = GetReader(language);
        // Đọc trên bản sao: trong lúc đọc (vài trăm ms - vài giây) người dùng vẫn sửa ảnh được.
        var copy = bitmap.Copy();
        SetTextSearchBusy(true, "Đang đọc chữ…");
        try
        {
            var progress = new Progress<double>(p =>
            {
                if (ReferenceEquals(cts, _ocrCts))
                {
                    TextSearchStatus.Text = $"Đang đọc chữ… {p * 100:0}%";
                }
            });
            var result = await Task.Run(() => reader.Read(copy, cts.Token, progress), cts.Token);
            var entry = new OcrEntry(bitmap, language, result, note is null ? reader.Name : $"{reader.Name} - {note}");
            _ocrCache.AddOrUpdate(vm, entry);
            Log.LogInformation("Đọc chữ {Width}×{Height} bằng {Reader}: {Lines} dòng, {Ms} ms",
                bitmap.Width, bitmap.Height, reader.Name, result.Lines.Count, (int)result.Elapsed.TotalMilliseconds);
            return entry;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            copy.Dispose();
            if (ReferenceEquals(cts, _ocrCts))
            {
                SetTextSearchBusy(false, null);
            }
        }
    }

    private void SetTextSearchBusy(bool busy, string? status)
    {
        TextSearchProgress.IsActive = busy;
        TextSearchProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (status is not null)
        {
            TextSearchStatus.Text = status;
        }
    }

    /// <summary>Cập nhật khung Tìm chữ theo tab / ô tìm / tuỳ chọn hiện tại (đọc chữ trước nếu chưa có). Gọi lại khi đang chạy
    /// thì lần cũ tự bỏ kết quả.</summary>
    private async Task RefreshTextSearchAsync()
    {
        if (!TextSearchOpen)
        {
            return;
        }
        int version = ++_textSearchVersion;
        var vm = _viewModel;
        OcrEntry? entry;
        try
        {
            entry = await EnsureOcrAsync(vm);
        }
        catch (Exception ex)
        {
            // Tesseract không có bản native cho ARM64, thiếu VC++ runtime / tessdata... - báo thay vì crash.
            Log.LogError(ex, "Không đọc được chữ");
            SetTextSearchBusy(false, $"Không đọc được chữ: {ex.Message}");
            return;
        }
        if (entry is null || version != _textSearchVersion || !ReferenceEquals(vm, _viewModel) || !TextSearchOpen)
        {
            return;
        }
        ShowTextResults(entry);
    }

    private void ShowTextResults(OcrEntry entry)
    {
        var ocr = entry.Result;
        string query = TextSearchBox.Text.Trim();
        TextResultList.Items.Clear();
        _textFocus = null;
        CopyAllTextButton.IsEnabled = ocr.Lines.Count > 0;
        string read = $"{ocr.Lines.Count} dòng · {entry.Reader} · {ocr.Elapsed.TotalSeconds:0.0} s";

        if (query.Length == 0)
        {
            // Ô tìm trống: liệt kê mọi dòng đọc được (xem OCR đọc đúng không, bấm để tới dòng đó).
            _textMatches = [];
            foreach (var line in ocr.Lines)
            {
                TextResultList.Items.Add(ResultItem(line.Text, ToRect(line.Bounds)));
            }
            TextSearchStatus.Text = ocr.Lines.Count == 0
                ? $"Không đọc được chữ nào ({entry.Reader}) - ảnh có chữ thì thử đổi ngôn ngữ."
                : $"Đã đọc {read}. Gõ chữ cần tìm.";
            UpdateHighlightButton();
            Canvas.Invalidate();
            return;
        }

        _textMatches = TextSearch.Find(ocr, query, TextMatchCaseBox.IsChecked == true, TextMatchDiacriticsBox.IsChecked == true);
        foreach (var match in _textMatches)
        {
            TextResultList.Items.Add(ResultItem($"{match.Number}. {(match.Approximate ? "≈ " : string.Empty)}{match.LineText}", ToRect(match.Bounds)));
        }
        TextSearchStatus.Text = _textMatches.Count switch
        {
            0 => $"Không thấy \"{query}\" ({read}). Chữ đọc từ ảnh có thể sai vài ký tự - thử đoạn ngắn hơn.",
            _ when _textMatches[0].Approximate => $"{_textMatches.Count} chỗ gần đúng (≈ - OCR có thể đọc sai 1 chữ) · {read}",
            _ => $"{_textMatches.Count} chỗ khớp · {read}",
        };
        UpdateHighlightButton();
        Canvas.Invalidate();
    }

    private static ListViewItem ResultItem(string text, SKRect bounds)
    {
        var item = new ListViewItem
        {
            Tag = bounds,
            Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis },
            Padding = new Thickness(12, 2, 12, 2),
            MinHeight = 28,
        };
        ToolTipService.SetToolTip(item, text);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, text);
        return item;
    }

    private void UpdateHighlightButton()
    {
        HighlightMatchesButton.IsEnabled = _textMatches.Count > 0;
        ((TextBlock)((StackPanel)HighlightMatchesButton.Content).Children[1]).Text =
            _textMatches.Count > 0 ? $"Tô Highlight ({_textMatches.Count})" : "Tô Highlight";
    }

    // ---- Điều khiển trong khung ----

    private void TextSearchBox_TextChanged(object sender, TextChangedEventArgs e) => _ = RefreshTextSearchAsync();

    private void TextSearchOption_Click(object sender, RoutedEventArgs e) => _ = RefreshTextSearchAsync();

    private void OcrLanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingOcrLanguage || OcrLanguageBox.SelectedItem is not ComboBoxItem { Tag: string tag }
            || !Enum.TryParse<FormLanguage>(tag, out var language) || language == _ocrLanguage)
        {
            return;
        }
        _ocrLanguage = language;
        OcrLanguageChanged?.Invoke(this, language);
        _ = RefreshTextSearchAsync();
    }

    /// <summary>Enter / Shift+Enter: chỗ khớp kế tiếp / trước (vòng lại); Esc: đóng khung. Luôn đánh dấu đã xử lý - không thì
    /// Enter lan lên Editor thành "Cắt ảnh theo vùng chọn".</summary>
    private void TextSearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            int count = TextResultList.Items.Count;
            if (count > 0)
            {
                int step = IsKeyDown(Windows.System.VirtualKey.Shift) ? -1 : 1;
                int current = TextResultList.SelectedIndex;
                TextResultList.SelectedIndex = current < 0 ? (step > 0 ? 0 : count - 1) : (current + step + count) % count;
                TextResultList.ScrollIntoView(TextResultList.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            CloseTextSearch();
            e.Handled = true;
        }
    }

    private void TextResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TextResultList.SelectedItem is ListViewItem { Tag: SKRect bounds })
        {
            _textFocus = bounds;
            ScrollImageRectIntoView(bounds);
            Canvas.Invalidate();
        }
    }

    /// <summary>Cuộn để vùng (toạ độ ảnh) nằm giữa khung nhìn, nếu đang khuất.</summary>
    private void ScrollImageRectIntoView(SKRect rect)
    {
        double scale = CurrentScale;
        double left = (rect.Left * _zoom + _viewOrigin.X) / scale, right = (rect.Right * _zoom + _viewOrigin.X) / scale;
        double top = (rect.Top * _zoom + _viewOrigin.Y) / scale, bottom = (rect.Bottom * _zoom + _viewOrigin.Y) / scale;
        double x = CanvasScroller.HorizontalOffset, y = CanvasScroller.VerticalOffset;
        double w = CanvasScroller.ViewportWidth, h = CanvasScroller.ViewportHeight;
        if (left >= x && right <= x + w && top >= y && bottom <= y + h)
        {
            return;
        }
        CanvasScroller.ChangeView(Math.Max(0, (left + right - w) / 2), Math.Max(0, (top + bottom - h) / 2), null, false);
    }

    // ---- Copy chữ / Tô Highlight ----

    private void CopyAllTextButton_Click(object sender, RoutedEventArgs e)
    {
        if (CachedOcr(_viewModel) is not { } entry)
        {
            return;
        }
        try
        {
            _clipboardService.CopyText(entry.Result.FullText);
            _viewModel.StatusText = $"Đã copy {entry.Result.Lines.Count} dòng chữ của ảnh.";
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Không copy được chữ");
            _viewModel.StatusText = $"Không copy được chữ: {ex.Message}";
        }
    }

    private void HighlightMatchesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_textMatches.Count == 0)
        {
            return;
        }
        // Nới 2 px quanh khung chữ OCR (khung sát nét chữ - tô đúng khít trông hụt).
        var areas = _textMatches.Select(m => SKRect.Inflate(ToRect(m.Bounds), 2, 2)).ToList();
        _viewModel.AddHighlights(areas, $"Tô Highlight {areas.Count} chỗ khớp \"{TextSearchBox.Text.Trim()}\"");
        Canvas.Invalidate();
    }

    private async void RegionCopyTextButton_Click(object sender, RoutedEventArgs e) => await CopyRegionTextAsync();

    /// <summary>Copy chữ trong vùng chọn: ảnh đã đọc chữ (khung Tìm chữ) thì lấy các từ nằm trong vùng - không phải đọc lại; chưa
    /// thì đọc riêng phần ảnh trong vùng.</summary>
    private async Task CopyRegionTextAsync()
    {
        if (_region is not { } region)
        {
            return;
        }
        var vm = _viewModel;
        try
        {
            string text;
            string reader;
            if (CachedOcr(vm) is { } entry)
            {
                text = TextInArea(entry.Result, region);
                reader = entry.Reader;
            }
            else
            {
                vm.StatusText = "Đang đọc chữ trong vùng chọn…";
                var (ocrReader, note) = GetReader(_ocrLanguage);
                using var crop = new SKBitmap(new SKImageInfo(region.Width, region.Height, OcrText.ColorType, OcrText.AlphaType));
                using (var canvas = new SKCanvas(crop))
                {
                    canvas.Clear(SKColors.White); // phần trong suốt (Ảnh mới "Trong suốt", đổ bóng) đọc như nền trắng
                    canvas.DrawBitmap(vm.Bitmap, SKRect.Create(region.Left, region.Top, region.Width, region.Height),
                        SKRect.Create(region.Width, region.Height));
                }
                var stopwatch = Stopwatch.StartNew();
                var result = await Task.Run(() => ocrReader.Read(crop));
                text = result.FullText;
                reader = note is null ? ocrReader.Name : $"{ocrReader.Name} - {note}";
                Log.LogInformation("Đọc chữ vùng {Width}×{Height} bằng {Reader}: {Lines} dòng, {Ms} ms",
                    region.Width, region.Height, ocrReader.Name, result.Lines.Count, stopwatch.ElapsedMilliseconds);
            }

            if (text.Length == 0)
            {
                vm.StatusText = $"Không đọc được chữ nào trong vùng chọn ({reader}) - có chữ thì thử đổi ngôn ngữ ở khung Tìm chữ.";
                return;
            }
            _clipboardService.CopyText(text);
            int lines = text.Split(Environment.NewLine).Length;
            vm.StatusText = $"Đã copy {lines} dòng chữ trong vùng chọn ({reader}).";
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Không đọc / copy được chữ trong vùng chọn");
            vm.StatusText = $"Không đọc được chữ: {ex.Message}";
        }
    }

    /// <summary>Chữ các từ có tâm nằm trong <paramref name="area"/>, giữ thứ tự dòng (mỗi dòng 1 dòng).</summary>
    private static string TextInArea(OcrResult ocr, SKRectI area)
    {
        var lines = ocr.Lines
            .Select(line => line.Words.Where(w => area.Contains(w.Bounds.MidX, w.Bounds.MidY)).Select(w => w.Text).ToList())
            .Where(words => words.Count > 0)
            .Select(words => OcrText.JoinLine(words));
        return string.Join(Environment.NewLine, lines);
    }

    // ---- Vẽ chỗ khớp lên canvas ----

    /// <summary>Gọi trong Canvas_PaintSurface (canvas đã theo toạ độ ảnh + zoom): chỗ khớp tô vàng mờ + viền cam, mục đang chọn
    /// viền đỏ đậm (ô tìm trống: chỉ khung dòng đang chọn).</summary>
    private void DrawTextSearchOverlay(SKCanvas canvas)
    {
        if (!TextSearchOpen)
        {
            return;
        }
        if (_textMatches.Count > 0)
        {
            using var fill = new SKPaint { Color = new SKColor(255, 214, 0, 80), Style = SKPaintStyle.Fill };
            using var stroke = new SKPaint { Color = new SKColor(230, 140, 0), Style = SKPaintStyle.Stroke, StrokeWidth = Px(1) };
            foreach (var match in _textMatches)
            {
                var r = SKRect.Inflate(ToRect(match.Bounds), Px(2), Px(2));
                canvas.DrawRect(r, fill);
                canvas.DrawRect(r, stroke);
            }
        }
        if (_textFocus is { } focus)
        {
            using var focusPaint = new SKPaint { Color = new SKColor(220, 30, 30), Style = SKPaintStyle.Stroke, StrokeWidth = Px(2.5f) };
            canvas.DrawRect(SKRect.Inflate(focus, Px(4), Px(4)), focusPaint);
        }
    }

    /// <summary>Ảnh của tab đổi (hoặc đổi tab) khi khung đang mở → bỏ chỗ khớp cũ (toạ độ không còn đúng) và đọc / tìm lại.</summary>
    private void OnTextSearchImageChanged()
    {
        if (TextSearchOpen)
        {
            ClearTextHits();
            TextResultList.Items.Clear();
            _ = RefreshTextSearchAsync();
        }
    }
}
