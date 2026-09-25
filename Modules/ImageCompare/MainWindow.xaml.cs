using System.ComponentModel;
using System.Runtime.InteropServices;
using ImageCompare.Models;
using ImageCompare.Services;
using ImageCompare.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.System;
using WinRT.Interop;

namespace ImageCompare;

/// <summary>Cửa sổ so sánh 2 ảnh. Code-behind lo phần View: hộp thoại, clipboard, kéo-thả, vẽ canvas
/// (SKXamlCanvas) và zoom / cuộn; trạng thái nằm ở <see cref="CompareViewModel"/>.
///
/// Toạ độ: mọi phép vẽ tính theo pixel thiết bị của canvas. <see cref="_zoom"/> = số pixel thiết bị cho 1
/// pixel ảnh, <see cref="_pan"/> = vị trí góc trên-trái ảnh A trong 1 ô xem (pane). Chế độ Cạnh nhau có 2
/// ô dùng CHUNG zoom / pan → zoom, cuộn đồng bộ; ảnh B vẽ lệch thêm <see cref="CompareViewModel.OffsetB"/>.</summary>
public sealed partial class MainWindow : Window
{
    private static readonly ILogger Log = AppLog.For(nameof(MainWindow));
    private static readonly SKColor BackgroundColor = new(0xEE, 0xEE, 0xEE);
    private static readonly SKColor ColorA = new(0x2B, 0x7B, 0xD6);
    private static readonly SKColor ColorB = new(0xD8, 0x64, 0x45);
    private const float PaneGap = 6;
    private const float MinZoom = 0.02f, MaxZoom = 32f;

    private readonly ImageFileService _fileService = new();
    private readonly ClipboardService _clipboard = new();

    private float _zoom = 1;
    private SKPoint _pan;
    private bool _fitPending;
    private bool _fitted; // đang "vừa cửa sổ" (chưa tự zoom / cuộn) → cửa sổ đổi cỡ thì vừa lại
    private SKRect _lastFitBounds; // khung nội dung lúc "vừa cửa sổ" gần nhất
    private bool _syncingOptions; // đang đồng bộ control từ ViewModel - bỏ qua sự kiện của control

    private enum DragKind { None, Pan, Swipe, Ignore }
    private SKPoint _ignoreStart; // toạ độ ảnh A - điểm bắt đầu kéo khoanh vùng bỏ qua
    private SKRect? _ignoreDraft;
    private DragKind _drag;
    private SKPoint _dragLast;

    public CompareViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        uint dpi = GetDpiForWindow(WindowNative.GetWindowHandle(this));
        double scale = dpi > 0 ? dpi / 96.0 : 1;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1280 * scale), (int)(820 * scale)));

        // Slider Minimum/Maximum gán qua code, không qua XAML (bài học XamlParseException ở ScreenCapture). Chặn
        // ValueChanged trong lúc này: đặt Minimum = 50 khi Value còn 0 làm WinUI đẩy Value lên 50 và bắn sự kiện, ghi
        // đè giá trị mặc định trong ViewModel (đã gặp: độ khớp tối thiểu thành 50% thay vì 90%).
        _syncingOptions = true;
        OpacitySlider.Minimum = 0;
        OpacitySlider.Maximum = 100;
        OpacitySlider.Value = ViewModel.OverlayOpacity * 100;
        ThresholdSlider.Minimum = 0;
        ThresholdSlider.Maximum = 50;
        ThresholdSlider.StepFrequency = 1;
        FindScoreSlider.Minimum = 50;
        FindScoreSlider.Maximum = 100;
        FindScoreSlider.StepFrequency = 1;
        _syncingOptions = false;
        SyncOptionControls();
        ViewModel.IgnoreRects.CollectionChanged += (_, _) =>
        {
            ClearIgnoreButton.Visibility = ViewModel.IgnoreRects.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            Canvas.Invalidate();
        };

        // Alt + mũi tên: dịch ảnh B 1 px, Alt + Shift + mũi tên: 10 px (chỉnh tay khi tự căn chưa đúng ý).
        foreach (var (key, dx, dy) in new[] { (VirtualKey.Left, -1, 0), (VirtualKey.Right, 1, 0), (VirtualKey.Up, 0, -1), (VirtualKey.Down, 0, 1) })
        {
            foreach (var shift in new[] { false, true })
            {
                var accelerator = new KeyboardAccelerator
                {
                    Key = key,
                    Modifiers = VirtualKeyModifiers.Menu | (shift ? VirtualKeyModifiers.Shift : VirtualKeyModifiers.None),
                };
                int step = shift ? 10 : 1;
                accelerator.Invoked += (_, args) =>
                {
                    args.Handled = true;
                    ViewModel.Nudge(dx * step, dy * step);
                };
                RootGrid.KeyboardAccelerators.Add(accelerator);
            }
        }

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        LoadFromCommandLine();
    }

    /// <summary>Đưa control tuỳ chọn về đúng giá trị trong ViewModel (lúc mở, hoặc khi ViewModel tự đổi -
    /// vd Alt + mũi tên chuyển sang "Chỉnh tay").</summary>
    private void SyncOptionControls()
    {
        _syncingOptions = true;
        ThresholdSlider.Value = ViewModel.ThresholdPercent;
        ThresholdText.Text = $"{ViewModel.ThresholdPercent:0}%";
        AntialiasBox.IsChecked = ViewModel.IgnoreAntialiasing;
        FindScoreSlider.Value = ViewModel.FindMinScore;
        FindScoreText.Text = $"{ViewModel.FindMinScore:0}%";
        IgnoreToolButton.IsEnabled = ViewModel.Align != Engine.AlignMode.Rows;
        if (!IgnoreToolButton.IsEnabled)
        {
            IgnoreToolButton.IsChecked = false;
        }
        AlignBox.SelectedItem = AlignBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == ViewModel.Align.ToString());
        TextSourceBox.SelectedIndex = ViewModel.TextUseA ? 0 : 1;
        TextDiacriticsBox.IsChecked = ViewModel.TextMatchDiacritics;
        TextCaseBox.IsChecked = ViewModel.TextMatchCase;
        _syncingOptions = false;
    }

    // ---- Tìm chữ ----

    private void TextSourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingOptions && TextSourceBox.SelectedIndex >= 0)
        {
            ViewModel.TextUseA = TextSourceBox.SelectedIndex == 0;
        }
    }

    private void TextQueryBox_TextChanged(object sender, TextChangedEventArgs e) => ViewModel.TextQuery = TextQueryBox.Text;

    private void TextOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!_syncingOptions)
        {
            ViewModel.TextMatchDiacritics = TextDiacriticsBox.IsChecked == true;
            ViewModel.TextMatchCase = TextCaseBox.IsChecked == true;
        }
    }

    /// <summary>Ctrl+F: sang chế độ Tìm chữ và đặt con trỏ vào ô tìm.</summary>
    private void FindTextAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ModeBar.SelectedItem = ModeBar.Items.First(i => (string)i.Tag == nameof(ViewMode.Text));
        TextQueryBox.Focus(FocusState.Keyboard);
        TextQueryBox.SelectAll();
    }

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Ocr is not { } ocr)
        {
            return;
        }
        var package = new DataPackage();
        package.SetText(ocr.FullText);
        Clipboard.SetContent(package);
        ViewModel.StatusText = $"Đã copy {ocr.Lines.Count} dòng chữ vào clipboard.";
    }

    private void AlignBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingOptions && AlignBox.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse<Engine.AlignMode>(tag, out var mode))
        {
            ViewModel.Align = mode;
        }
    }

    private void ThresholdSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        ThresholdText.Text = $"{e.NewValue:0}%";
        if (!_syncingOptions)
        {
            ViewModel.ThresholdPercent = e.NewValue;
        }
    }

    private void FindScoreSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        FindScoreText.Text = $"{e.NewValue:0}%";
        if (!_syncingOptions)
        {
            ViewModel.FindMinScore = e.NewValue;
        }
    }

    /// <summary>Bật công cụ khoanh vùng bỏ qua (chế độ Khác biệt): kéo chuột trái = thêm vùng, bấm vào vùng có sẵn =
    /// xoá; kéo để cuộn ảnh thì dùng chuột phải / giữa.</summary>
    private void IgnoreToolButton_Click(object sender, RoutedEventArgs e) =>
        ViewModel.StatusText = IgnoreToolButton.IsChecked == true
            ? "Kéo chuột trái trên ảnh để khoanh vùng bỏ qua; bấm vào 1 vùng bỏ qua để xoá. Chuột phải / giữa để cuộn ảnh."
            : "Đã tắt khoanh vùng bỏ qua.";

    /// <summary>Màu nhãn số trong danh sách kết quả (x:Bind không tự đổi chuỗi màu sang Brush).</summary>
    public static Microsoft.UI.Xaml.Media.SolidColorBrush BrushOf(string hex)
    {
        var c = SKColor.Parse(hex);
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue));
    }

    private void AntialiasBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_syncingOptions)
        {
            ViewModel.IgnoreAntialiasing = AntialiasBox.IsChecked == true;
        }
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hwnd);

    private IntPtr Hwnd => WindowNative.GetWindowHandle(this);
    private float RasterScale => (float)(Canvas.XamlRoot?.RasterizationScale ?? 1.0);

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CompareViewModel.ImageA) or nameof(CompareViewModel.ImageB):
                ClearAButton.Visibility = ViewModel.ImageA is null ? Visibility.Collapsed : Visibility.Visible;
                ClearBButton.Visibility = ViewModel.ImageB is null ? Visibility.Collapsed : Visibility.Visible;
                FitToWindow();
                break;
            // Kết quả mới mà khung nội dung đổi (ảnh mới, đổi sang / từ căn theo dòng...) → vừa cửa sổ lại; chỉ đổi
            // ngưỡng thì giữ nguyên zoom / vị trí đang soi.
            case nameof(CompareViewModel.Painter) or nameof(CompareViewModel.FindResult) when ContentBounds() != _lastFitBounds:
                FitToWindow();
                break;
            case nameof(CompareViewModel.TextTarget) when ViewModel.Mode == ViewMode.Text && ContentBounds() != _lastFitBounds:
                FitToWindow();
                break;
            case nameof(CompareViewModel.Align) or nameof(CompareViewModel.TextUseA):
                SyncOptionControls();
                break;
        }
        Canvas.Invalidate();
    }

    // ---- Kết quả / xuất ----

    private void RegionList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.HighlightedItem = (RegionList.SelectedItem as ResultItem)?.Number ?? 0;

    /// <summary>Bấm 1 kết quả: chọn + phóng để nó chiếm ~40% ô xem (tối đa 800%, Tìm chữ 300%), đặt vào giữa.</summary>
    private void RegionList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ResultItem item)
        {
            return;
        }
        ViewModel.HighlightedItem = item.Number;
        var pane = CurrentPanes()[0];
        var box = item.Bounds;
        // Tìm chữ: tối đa 300% - 1 từ ngắn phóng 800% thì vỡ hạt, mất chữ xung quanh.
        float maxZoom = ViewModel.Mode == ViewMode.Text ? 3f : 8f;
        float zoom = Math.Clamp(Math.Min(pane.Width * 0.4f / Math.Max(1, box.Width), pane.Height * 0.4f / Math.Max(1, box.Height)), MinZoom, maxZoom);
        _zoom = zoom;
        _pan = new SKPoint(pane.Width / 2 - (box.Left + box.Width / 2f) * zoom, pane.Height / 2 - (box.Top + box.Height / 2f) * zoom);
        _fitted = false;
        UpdateZoomText();
        Canvas.Invalidate();
    }

    private string ReportBaseName() =>
        $"so-sanh {Path.GetFileNameWithoutExtension(ViewModel.ImageA?.Name ?? "A")} - {Path.GetFileNameWithoutExtension(ViewModel.ImageB?.Name ?? "B")}";

    private async void CopyDiff_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Painter is not { } painter)
        {
            return;
        }
        try
        {
            using var image = painter.Render();
            await _clipboard.CopyBitmapAsync(image);
            ViewModel.StatusText = "Đã copy ảnh khác biệt vào clipboard.";
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = $"Không copy được: {ex.Message}";
            Log.LogError(ex, "Không copy được ảnh khác biệt");
        }
    }

    private async void SavePng_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Painter is not { } painter)
        {
            return;
        }
        try
        {
            using var image = painter.Render();
            if (await _fileService.SavePngAsync(image, Hwnd, ReportBaseName()) is { } path)
            {
                ViewModel.StatusText = $"Đã lưu: {path}";
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = $"Không lưu được: {ex.Message}";
            Log.LogError(ex, "Không lưu được ảnh khác biệt");
        }
    }

    private async void SaveHtml_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Painter is not { } painter || ViewModel.ImageA is not { } a || ViewModel.ImageB is not { } b)
        {
            return;
        }
        try
        {
            var options = ViewModel.CurrentOptions;
            ViewModel.StatusText = "Đang tạo báo cáo…";
            string html = await Task.Run(() => Engine.HtmlReport.Build(a.Name, a.Bitmap, b.Name, b.Bitmap, options, painter));
            if (await _fileService.SaveHtmlAsync(html, Hwnd, ReportBaseName()) is { } path)
            {
                ViewModel.StatusText = $"Đã xuất báo cáo: {path}";
                Log.LogInformation("Xuất báo cáo {Path}", path);
            }
            else
            {
                ViewModel.StatusText = "Đã huỷ xuất báo cáo.";
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = $"Không xuất được báo cáo: {ex.Message}";
            Log.LogError(ex, "Không xuất được báo cáo HTML");
        }
    }

    // ---- Nạp ảnh ----

    /// <summary><c>ImageCompare.exe a.png b.png</c> → mở sẵn 2 ảnh (vd gọi từ tool khác / "Open with").</summary>
    private async void LoadFromCommandLine()
    {
        var files = Environment.GetCommandLineArgs().Skip(1).Where(File.Exists).Take(2).ToList();
        for (int i = 0; i < files.Count; i++)
        {
            await LoadFileAsync(slotA: i == 0, files[i]);
        }
    }

    private async Task LoadFileAsync(bool slotA, string path)
    {
        try
        {
            var bitmap = await Task.Run(() => ImageFileService.LoadImage(path));
            ViewModel.SetImage(slotA, new LoadedImage(bitmap, Path.GetFileName(path), path));
            Log.LogInformation("Mở ảnh {Slot}: {Path} ({W}x{H})", slotA ? "A" : "B", path, bitmap.Width, bitmap.Height);
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = $"Không mở được {Path.GetFileName(path)}: {ex.Message}";
            Log.LogError(ex, "Không mở được ảnh {Path}", path);
        }
    }

    private async Task PasteAsync(bool slotA)
    {
        try
        {
            if (await _clipboard.GetBitmapAsync() is not { } pasted)
            {
                ViewModel.StatusText = "Clipboard không có ảnh để dán.";
                return;
            }
            ViewModel.SetImage(slotA, new LoadedImage(pasted.Bitmap, pasted.Name, null));
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = $"Không dán được ảnh: {ex.Message}";
            Log.LogError(ex, "Không dán được ảnh từ clipboard");
        }
    }

    private async Task OpenAsync(bool slotA)
    {
        if (await _fileService.PickImageAsync(Hwnd) is { } path)
        {
            await LoadFileAsync(slotA, path);
        }
    }

    private async void OpenA_Click(object sender, RoutedEventArgs e) => await OpenAsync(slotA: true);
    private async void OpenB_Click(object sender, RoutedEventArgs e) => await OpenAsync(slotA: false);
    private async void PasteA_Click(object sender, RoutedEventArgs e) => await PasteAsync(slotA: true);
    private async void PasteB_Click(object sender, RoutedEventArgs e) => await PasteAsync(slotA: false);

    private async void PasteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTyping())
        {
            return; // đang gõ trong ô Tìm chữ: để ô đó dán chữ
        }
        args.Handled = true;
        bool replacingB = !ViewModel.NextSlotIsA && ViewModel.ImageB is not null;
        await PasteAsync(ViewModel.NextSlotIsA);
        if (replacingB && ViewModel.StatusText.StartsWith("Ảnh B", StringComparison.Ordinal))
        {
            ViewModel.StatusText += "  (đã thay ảnh B - muốn dán vào A: Ctrl+Shift+V hoặc nút Dán ở ô A)";
        }
    }

    /// <summary>Ctrl+Shift+V: dán thẳng vào ô A (Ctrl+V vào ô trống trước, 2 ô đều có ảnh thì vào B).</summary>
    private async void PasteAAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTyping())
        {
            return;
        }
        args.Handled = true;
        await PasteAsync(slotA: true);
    }

    private bool IsTyping() => FocusManager.GetFocusedElement(Content.XamlRoot) is TextBox;

    private void ClearA_Click(object sender, RoutedEventArgs e) => ViewModel.ClearImage(slotA: true);
    private void ClearB_Click(object sender, RoutedEventArgs e) => ViewModel.ClearImage(slotA: false);

    private void Slot_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = ReferenceEquals(sender, SlotA) ? "Ảnh A" : ReferenceEquals(sender, SlotB) ? "Ảnh B" : "So sánh";
        }
    }

    private async void Slot_Drop(object sender, DragEventArgs e)
    {
        var files = await DroppedImagesAsync(e);
        if (files.Count > 0)
        {
            await LoadFileAsync(slotA: ReferenceEquals(sender, SlotA), files[0]);
        }
    }

    /// <summary>Thả 2 file cùng lúc → A và B. Thả 1 file: Cạnh nhau thì nửa trái = A, nửa phải = B; chế độ
    /// khác thì vào ô A nếu còn trống, không thì B.</summary>
    private async void Canvas_Drop(object sender, DragEventArgs e)
    {
        var files = await DroppedImagesAsync(e);
        if (files.Count >= 2)
        {
            await LoadFileAsync(slotA: true, files[0]);
            await LoadFileAsync(slotA: false, files[1]);
        }
        else if (files.Count == 1)
        {
            bool slotA = ViewModel.Mode == ViewMode.SideBySide
                ? e.GetPosition(Canvas).X < Canvas.ActualWidth / 2
                : ViewModel.NextSlotIsA;
            await LoadFileAsync(slotA, files[0]);
        }
    }

    private static async Task<List<string>> DroppedImagesAsync(DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return [];
        }
        var items = await e.DataView.GetStorageItemsAsync();
        return items.OfType<StorageFile>()
            .Where(f => ImageFileService.ImageExtensions.Contains(f.FileType.ToLowerInvariant()))
            .Select(f => f.Path)
            .ToList();
    }

    // ---- Chế độ xem ----

    private void ModeBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string tag && Enum.TryParse<ViewMode>(tag, out var mode))
        {
            bool wasSplit = ViewModel.Mode == ViewMode.SideBySide;
            ViewModel.Mode = mode;
            // Sự kiện có thể tới lúc InitializeComponent chưa gán xong các control bên dưới.
            if (OverlayOptions is null || SwipeHint is null || DiffOptions is null || ResultPanel is null || FindOptions is null
                || TextOptions is null || ExportButtons is null)
            {
                return;
            }
            OverlayOptions.Visibility = mode == ViewMode.Overlay ? Visibility.Visible : Visibility.Collapsed;
            SwipeHint.Visibility = mode == ViewMode.Swipe ? Visibility.Visible : Visibility.Collapsed;
            DiffOptions.Visibility = mode == ViewMode.Diff ? Visibility.Visible : Visibility.Collapsed;
            FindOptions.Visibility = mode == ViewMode.Find ? Visibility.Visible : Visibility.Collapsed;
            TextOptions.Visibility = mode == ViewMode.Text ? Visibility.Visible : Visibility.Collapsed;
            ResultPanel.Visibility = mode is ViewMode.Diff or ViewMode.Find or ViewMode.Text ? Visibility.Visible : Visibility.Collapsed;
            ExportButtons.Visibility = mode == ViewMode.Diff ? Visibility.Visible : Visibility.Collapsed; // xuất = ảnh khác biệt
            // Giữa các chế độ 1 ô cùng khung nội dung giữ nguyên zoom / vị trí để soi cùng 1 chỗ; Cạnh nhau ↔ 1 ô (bề
            // rộng ô xem đổi gấp đôi) hoặc khung nội dung đổi (ảnh ghép, ảnh được tìm) thì vừa cửa sổ lại.
            if (wasSplit != (mode == ViewMode.SideBySide) || ContentBounds() != _lastFitBounds)
            {
                FitToWindow();
            }
        }
    }

    private void OpacitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) =>
        ViewModel.OverlayOpacity = e.NewValue / 100;

    // ---- Zoom / cuộn ----

    /// <summary>Các ô xem trên canvas (pixel thiết bị): Cạnh nhau = 2 ô trái / phải, còn lại 1 ô.</summary>
    private SKRect[] Panes(float width, float height)
    {
        if (ViewModel.Mode != ViewMode.SideBySide)
        {
            return [SKRect.Create(width, height)];
        }
        float half = (width - PaneGap * RasterScale) / 2;
        return [SKRect.Create(half, height), SKRect.Create(width - half, 0, half, height)];
    }

    private SKRect[] CurrentPanes() =>
        Panes((float)(Canvas.ActualWidth * RasterScale), (float)(Canvas.ActualHeight * RasterScale));

    /// <summary>Khung bao nội dung theo toạ độ ảnh A: hợp của ảnh A và ảnh B (đặt lệch OffsetB).</summary>
    /// <summary>Khung nội dung đang vẽ: Khác biệt = khung ảnh khác biệt (ảnh ghép nếu căn theo dòng); Tìm ảnh con = ảnh
    /// được tìm; còn lại = A ∪ B (B đặt lệch OffsetB).</summary>
    private SKRect ContentBounds()
    {
        if (ViewModel.Mode == ViewMode.Diff && ViewModel.Painter is { } view)
        {
            return view.Bounds;
        }
        if (ViewModel.Mode == ViewMode.Find && ViewModel.FindTarget is { } target)
        {
            return SKRect.Create(target.Bitmap.Width, target.Bitmap.Height);
        }
        if (ViewModel.Mode == ViewMode.Text)
        {
            return ViewModel.TextTarget is { } t ? SKRect.Create(t.Bitmap.Width, t.Bitmap.Height) : SKRect.Empty;
        }
        var bounds = SKRect.Empty;
        if (ViewModel.ImageA is { } a)
        {
            bounds = SKRect.Create(a.Bitmap.Width, a.Bitmap.Height);
        }
        if (ViewModel.ImageB is { } b)
        {
            var rb = SKRect.Create(ViewModel.OffsetB.X, ViewModel.OffsetB.Y, b.Bitmap.Width, b.Bitmap.Height);
            bounds = bounds.IsEmpty ? rb : SKRect.Union(bounds, rb);
        }
        return bounds;
    }

    /// <summary>Vừa cửa sổ: cả 2 ảnh lọt trong ô xem, không phóng to quá 100% (ảnh nhỏ giữ nét).</summary>
    private void FitToWindow()
    {
        var panes = CurrentPanes();
        var bounds = ContentBounds();
        if (bounds.IsEmpty || panes[0].Width < 10 || panes[0].Height < 10)
        {
            _fitPending = true;
            return;
        }
        _fitPending = false;
        _lastFitBounds = bounds;
        float margin = 12 * RasterScale;
        var pane = panes[0];
        _zoom = Math.Clamp(Math.Min((pane.Width - 2 * margin) / bounds.Width, (pane.Height - 2 * margin) / bounds.Height), MinZoom, 1f);
        _pan = new SKPoint((pane.Width - bounds.Width * _zoom) / 2 - bounds.Left * _zoom,
            (pane.Height - bounds.Height * _zoom) / 2 - bounds.Top * _zoom);
        _fitted = true;
        UpdateZoomText();
        Canvas.Invalidate();
    }

    /// <summary>Đổi zoom giữ nguyên điểm ảnh nằm dưới <paramref name="anchor"/> (toạ độ trong ô xem).</summary>
    private void ZoomAt(float newZoom, SKPoint anchor)
    {
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
        var imagePoint = new SKPoint((anchor.X - _pan.X) / _zoom, (anchor.Y - _pan.Y) / _zoom);
        _zoom = newZoom;
        _pan = new SKPoint(anchor.X - imagePoint.X * _zoom, anchor.Y - imagePoint.Y * _zoom);
        _fitted = false;
        UpdateZoomText();
        Canvas.Invalidate();
    }

    private SKPoint PaneCenter()
    {
        var pane = CurrentPanes()[0];
        return new SKPoint(pane.Width / 2, pane.Height / 2);
    }

    private void UpdateZoomText() => ZoomText.Text = $"{_zoom * 100:0.#}%";

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomAt(_zoom * 1.25f, PaneCenter());
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomAt(_zoom / 1.25f, PaneCenter());
    private void Fit_Click(object sender, RoutedEventArgs e) => FitToWindow();
    private void ActualSize_Click(object sender, RoutedEventArgs e) => ZoomAt(1f, PaneCenter());

    private void FitAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        FitToWindow();
    }

    private void ActualSizeAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ZoomAt(1f, PaneCenter());
    }

    private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Chưa tự zoom / cuộn (vd vừa mở rồi phóng to cửa sổ, bật / tắt bảng kết quả) → vừa lại theo cỡ mới.
        if (_fitPending || _fitted)
        {
            FitToWindow();
        }
    }

    // ---- Chuột ----

    /// <summary>Vị trí con trỏ: ô xem đang trỏ + toạ độ trong ô đó (pixel thiết bị).</summary>
    private (int Pane, SKPoint Local) HitPane(PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(Canvas).Position;
        var device = new SKPoint((float)p.X * RasterScale, (float)p.Y * RasterScale);
        var panes = CurrentPanes();
        int index = panes.Length > 1 && device.X >= panes[1].Left ? 1 : 0;
        return (index, new SKPoint(device.X - panes[index].Left, device.Y - panes[index].Top));
    }

    private SKPoint ToImage(SKPoint local) => new((local.X - _pan.X) / _zoom, (local.Y - _pan.Y) / _zoom);

    private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Canvas);
        var (_, local) = HitPane(e);
        // Thanh trượt: chuột trái kéo vạch chia (bấm chỗ nào vạch nhảy tới đó). Chuột phải / giữa, hoặc chuột
        // trái ở chế độ khác = kéo để cuộn ảnh.
        if (ViewModel.Mode == ViewMode.Swipe && point.Properties.IsLeftButtonPressed)
        {
            _drag = DragKind.Swipe;
            ViewModel.SwipeX = ToImage(local).X;
        }
        // Khoanh vùng bỏ qua (chế độ Khác biệt, đã bật nút "Vùng bỏ qua"): toạ độ canvas = toạ độ ảnh A.
        else if (ViewModel.Mode == ViewMode.Diff && IgnoreToolButton.IsChecked == true && point.Properties.IsLeftButtonPressed)
        {
            _drag = DragKind.Ignore;
            _ignoreStart = ToImage(local);
            _ignoreDraft = null;
        }
        else
        {
            _drag = DragKind.Pan;
        }
        _dragLast = local;
        Canvas.CapturePointer(e.Pointer);
    }

    private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var (_, local) = HitPane(e);
        switch (_drag)
        {
            case DragKind.Pan:
                _pan = new SKPoint(_pan.X + local.X - _dragLast.X, _pan.Y + local.Y - _dragLast.Y);
                _fitted = false;
                Canvas.Invalidate();
                break;
            case DragKind.Swipe:
                ViewModel.SwipeX = ToImage(local).X;
                break;
            case DragKind.Ignore:
                var p = ToImage(local);
                _ignoreDraft = new SKRect(Math.Min(p.X, _ignoreStart.X), Math.Min(p.Y, _ignoreStart.Y), Math.Max(p.X, _ignoreStart.X), Math.Max(p.Y, _ignoreStart.Y));
                Canvas.Invalidate();
                break;
        }
        _dragLast = local;
        UpdateCursorText(ToImage(local));
    }

    private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_drag == DragKind.Ignore)
        {
            // Kéo ≥ 4 px = thêm vùng bỏ qua (cắt theo ảnh A); bấm không kéo = xoá vùng bỏ qua tại chỗ bấm.
            if (_ignoreDraft is { } draft && draft.Width >= 4 && draft.Height >= 4 && ViewModel.ImageA is { } a)
            {
                var rect = SKRectI.Intersect(SKRectI.Round(draft), SKRectI.Create(a.Bitmap.Width, a.Bitmap.Height));
                if (!rect.IsEmpty)
                {
                    ViewModel.AddIgnoreRect(rect);
                }
            }
            else
            {
                ViewModel.RemoveIgnoreRectAt((int)_ignoreStart.X, (int)_ignoreStart.Y);
            }
            _ignoreDraft = null;
            Canvas.Invalidate();
        }
        _drag = DragKind.None;
        Canvas.ReleasePointerCapture(e.Pointer);
    }

    private void Canvas_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_drag == DragKind.None)
        {
            CursorText.Text = string.Empty;
        }
    }

    /// <summary>Lăn = cuộn dọc, Shift + lăn (hoặc lăn ngang) = cuộn ngang, Ctrl + lăn = zoom quanh con trỏ.</summary>
    private void Canvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Canvas);
        int delta = point.Properties.MouseWheelDelta;
        var modifiers = e.KeyModifiers;
        var (_, local) = HitPane(e);
        if (modifiers.HasFlag(VirtualKeyModifiers.Control))
        {
            ZoomAt(_zoom * MathF.Pow(1.15f, delta / 120f), local);
        }
        else if (point.Properties.IsHorizontalMouseWheel || modifiers.HasFlag(VirtualKeyModifiers.Shift))
        {
            _pan = new SKPoint(_pan.X + (point.Properties.IsHorizontalMouseWheel ? -delta : delta) * RasterScale, _pan.Y);
            _fitted = false;
            Canvas.Invalidate();
        }
        else
        {
            _pan = new SKPoint(_pan.X, _pan.Y + delta * RasterScale);
            _fitted = false;
            Canvas.Invalidate();
        }
        e.Handled = true;
    }

    /// <summary>Toạ độ pixel dưới con trỏ + màu ở ảnh A và B tại đó - soi nhanh 1 điểm khác nhau thế nào.</summary>
    private void UpdateCursorText(SKPoint image)
    {
        int x = (int)Math.Floor(image.X), y = (int)Math.Floor(image.Y);
        static string ColorAt(LoadedImage? img, int px, int py) =>
            img is not null && px >= 0 && py >= 0 && px < img.Bitmap.Width && py < img.Bitmap.Height
                ? $"#{img.Bitmap.GetPixel(px, py).ToString()[3..]}"
                : "-------";
        if (ViewModel.Mode is ViewMode.Find or ViewMode.Text)
        {
            var target = ViewModel.Mode == ViewMode.Find ? ViewModel.FindTarget : ViewModel.TextTarget;
            CursorText.Text = $"x {x,5}  y {y,5}   {ColorAt(target, x, y)}";
            return;
        }
        if (ViewModel.Mode == ViewMode.Diff && ViewModel.Painter?.Stats.Align == Engine.AlignMode.Rows)
        {
            CursorText.Text = $"x {x,5}  y {y,5}   (toạ độ ảnh ghép)";
            return;
        }
        var offset = ViewModel.OffsetB;
        CursorText.Text = $"x {x,5}  y {y,5}   A {ColorAt(ViewModel.ImageA, x, y)}   B {ColorAt(ViewModel.ImageB, x - offset.X, y - offset.Y)}";
    }

    // ---- Vẽ ----

    private void Canvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(BackgroundColor);
        var panes = Panes(e.Info.Width, e.Info.Height);
        // Phóng to từ 200% trở lên: vẽ không nội suy để thấy rõ từng pixel khác nhau.
        using var imagePaint = new SKPaint { FilterQuality = _zoom >= 2 ? SKFilterQuality.None : SKFilterQuality.Medium };

        for (int i = 0; i < panes.Length; i++)
        {
            var pane = panes[i];
            canvas.Save();
            canvas.ClipRect(pane);
            canvas.Translate(pane.Left + _pan.X, pane.Top + _pan.Y);
            canvas.Scale(_zoom);
            switch (ViewModel.Mode)
            {
                case ViewMode.Diff when ViewModel.Painter is { } painter:
                    painter.Draw(canvas, 1 / _zoom, imagePaint.FilterQuality, ViewModel.HighlightedItem);
                    if (painter.Stats.Align != Engine.AlignMode.Rows)
                    {
                        DrawIgnoreDraft(canvas);
                    }
                    break;
                case ViewMode.Find:
                    DrawImage(canvas, ViewModel.FindTarget, SKPointI.Empty, imagePaint);
                    DrawMatches(canvas);
                    break;
                case ViewMode.Text:
                    DrawImage(canvas, ViewModel.TextTarget, SKPointI.Empty, imagePaint);
                    DrawTextBoxes(canvas);
                    break;
                case ViewMode.Diff:
                    // Đang so sánh (hoặc thiếu ảnh): hiện ảnh đang có cho khỏi trống.
                    DrawImage(canvas, ViewModel.ImageB ?? ViewModel.ImageA, ViewModel.ImageB is null ? SKPointI.Empty : ViewModel.OffsetB, imagePaint);
                    break;
                case ViewMode.SideBySide:
                    DrawImage(canvas, i == 0 ? ViewModel.ImageA : ViewModel.ImageB, i == 0 ? SKPointI.Empty : ViewModel.OffsetB, imagePaint);
                    break;
                case ViewMode.Overlay:
                    DrawImage(canvas, ViewModel.ImageA, SKPointI.Empty, imagePaint);
                    using (var faded = new SKPaint { FilterQuality = imagePaint.FilterQuality, Color = SKColors.White.WithAlpha((byte)(ViewModel.OverlayOpacity * 255)) })
                    {
                        DrawImage(canvas, ViewModel.ImageB, ViewModel.OffsetB, faded);
                    }
                    break;
                case ViewMode.Swipe:
                    DrawImage(canvas, ViewModel.ImageA, SKPointI.Empty, imagePaint);
                    canvas.Save();
                    canvas.ClipRect(new SKRect((float)ViewModel.SwipeX, -1e7f, 1e7f, 1e7f));
                    DrawImage(canvas, ViewModel.ImageB, ViewModel.OffsetB, imagePaint);
                    canvas.Restore();
                    break;
            }
            canvas.Restore();

            DrawPaneLabels(canvas, pane, i);
        }

        if (ViewModel.Mode == ViewMode.Swipe && ViewModel.HasBothImages)
        {
            DrawSwipeDivider(canvas, panes[0]);
        }
    }

    /// <summary>Vùng bỏ qua đang kéo (chưa thả chuột).</summary>
    private void DrawIgnoreDraft(SKCanvas canvas)
    {
        if (_ignoreDraft is { } draft)
        {
            Engine.DiffPainter.DrawIgnoreRects(canvas, [SKRectI.Round(draft)], 1 / _zoom);
        }
    }

    /// <summary>Chế độ Tìm ảnh con: khung xanh lá + số thứ tự + % khớp cho từng chỗ tìm thấy.</summary>
    private void DrawMatches(SKCanvas canvas)
    {
        if (ViewModel.FindResult is not { } result)
        {
            return;
        }
        float px = 1 / _zoom;
        var green = new SKColor(0x1A, 0x7F, 0x37);
        using var box = new SKPaint { Color = green, Style = SKPaintStyle.Stroke, StrokeWidth = 2 * px, IsAntialias = true };
        using var strong = new SKPaint { Color = new SKColor(0xFF, 0xC1, 0x07), Style = SKPaintStyle.Stroke, StrokeWidth = 3 * px, IsAntialias = true };
        using var tagFill = new SKPaint { Color = green, IsAntialias = true };
        using var text = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 12 * px, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) };
        foreach (var m in result.Matches)
        {
            var r = SKRect.Inflate(m.Bounds, 2 * px, 2 * px);
            canvas.DrawRect(r, m.Number == ViewModel.HighlightedItem ? strong : box);
            string label = $"{m.Number} · {m.Score * 100:0.#}%";
            var tag = SKRect.Create(r.Left, r.Top - 16 * px, text.MeasureText(label) + 8 * px, 16 * px);
            if (tag.Top < 0)
            {
                tag.Offset(0, 16 * px);
            }
            canvas.DrawRect(tag, tagFill);
            canvas.DrawText(label, tag.Left + 4 * px, tag.Bottom - 4 * px, text);
        }
    }

    /// <summary>Chế độ Tìm chữ: đang tìm → tô vàng kiểu bút dạ các chỗ khớp; ô tìm trống → khung xanh mảnh quanh
    /// từng dòng đọc được (thấy được app đã đọc những chỗ nào). Mục đang chọn: khung vàng đậm + số.</summary>
    private void DrawTextBoxes(SKCanvas canvas)
    {
        if (ViewModel.Ocr is not { } ocr)
        {
            return;
        }
        float px = 1 / _zoom;
        var amber = new SKColor(0xBF, 0x87, 0x00);
        using var strong = new SKPaint { Color = new SKColor(0xFF, 0xC1, 0x07), Style = SKPaintStyle.Stroke, StrokeWidth = 3 * px, IsAntialias = true };
        if (ViewModel.TextMatches is not { } matches)
        {
            using var line = new SKPaint { Color = new SKColor(0x2B, 0x7B, 0xD6, 110), Style = SKPaintStyle.Stroke, StrokeWidth = 1 * px };
            for (int i = 0; i < ocr.Lines.Count; i++)
            {
                var r = SKRect.Inflate(ocr.Lines[i].Bounds, 2 * px, 2 * px);
                canvas.DrawRect(r, i + 1 == ViewModel.HighlightedItem ? strong : line);
            }
            return;
        }
        using var fill = new SKPaint { Color = new SKColor(0xFF, 0xD6, 0x00, 90) };
        using var border = new SKPaint { Color = amber, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * px, IsAntialias = true };
        using var tagFill = new SKPaint { Color = amber, IsAntialias = true };
        using var text = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 12 * px, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) };
        foreach (var m in matches)
        {
            var r = SKRect.Inflate(m.Bounds, 2 * px, 2 * px);
            canvas.DrawRect(r, fill);
            canvas.DrawRect(r, m.Number == ViewModel.HighlightedItem ? strong : border);
            // Số thứ tự chỉ khi ít kết quả (nhiều thì rối) hoặc là mục đang chọn.
            if (matches.Count <= 30 || m.Number == ViewModel.HighlightedItem)
            {
                string label = m.Number.ToString();
                var tag = SKRect.Create(r.Left, r.Top - 15 * px, text.MeasureText(label) + 8 * px, 15 * px);
                if (tag.Top < 0)
                {
                    tag.Offset(0, r.Height + 15 * px);
                }
                canvas.DrawRect(tag, tagFill);
                canvas.DrawText(label, tag.Left + 4 * px, tag.Bottom - 3.5f * px, text);
            }
        }
    }

    private static void DrawImage(SKCanvas canvas, LoadedImage? image, SKPointI offset, SKPaint paint)
    {
        if (image is null)
        {
            return;
        }
        var rect = SKRect.Create(offset.X, offset.Y, image.Bitmap.Width, image.Bitmap.Height);
        canvas.DrawBitmap(image.Bitmap, rect, paint);
        using var border = new SKPaint { Color = new SKColor(0, 0, 0, 60), Style = SKPaintStyle.Stroke, StrokeWidth = 0 };
        canvas.DrawRect(rect, border);
    }

    /// <summary>Nhãn A / B ở góc ô xem, hoặc hướng dẫn khi ô còn trống.</summary>
    private void DrawPaneLabels(SKCanvas canvas, SKRect pane, int index)
    {
        float s = RasterScale;
        using var font = new SKPaint { IsAntialias = true, TextSize = 13 * s, Typeface = SKTypeface.FromFamilyName("Segoe UI") };
        bool split = ViewModel.Mode == ViewMode.SideBySide;

        void Tag(string text, SKColor color, float x, float y)
        {
            font.Color = SKColors.White;
            float width = font.MeasureText(text);
            using var bg = new SKPaint { Color = color, IsAntialias = true };
            canvas.DrawRoundRect(SKRect.Create(x, y, width + 12 * s, 20 * s), 4 * s, 4 * s, bg);
            canvas.DrawText(text, x + 6 * s, y + 14.5f * s, font);
        }

        if (split)
        {
            var image = index == 0 ? ViewModel.ImageA : ViewModel.ImageB;
            Tag(index == 0 ? "A" : "B", index == 0 ? ColorA : ColorB, pane.Left + 8 * s, pane.Top + 8 * s);
            if (image is null)
            {
                DrawHint(canvas, pane, font, $"Ảnh {(index == 0 ? "A" : "B")}: kéo-thả file ảnh vào đây, hoặc bấm Mở / Dán ở trên");
            }
        }
        else
        {
            if (ViewModel.Mode == ViewMode.Swipe)
            {
                Tag("A", ColorA, pane.Left + 8 * s, pane.Top + 8 * s);
                Tag("B", ColorB, pane.Right - 32 * s, pane.Top + 8 * s);
            }
            else if (ViewModel.Mode == ViewMode.Overlay)
            {
                Tag("A + B", ColorA, pane.Left + 8 * s, pane.Top + 8 * s);
            }
            else
            {
                if (ViewModel.Mode == ViewMode.Text)
                {
                    if (ViewModel.TextTarget is { } t)
                    {
                        bool isA = ReferenceEquals(t, ViewModel.ImageA);
                        Tag($"Chữ trong ảnh {(isA ? "A" : "B")}", isA ? ColorA : ColorB, pane.Left + 8 * s, pane.Top + 8 * s);
                    }
                    else
                    {
                        DrawHint(canvas, pane, font, "Kéo-thả 1 file ảnh vào đây (hoặc Mở / Dán ở trên) để đọc chữ trong ảnh");
                    }
                    return; // Tìm chữ chỉ cần 1 ảnh - không nhắc đưa đủ 2 ảnh
                }
                if (ViewModel.Mode == ViewMode.Find)
                {
                    Tag(ViewModel.FindResult is { Swapped: true } ? "Tìm A trong B" : "Tìm B trong A", new SKColor(0x1A, 0x7F, 0x37), pane.Left + 8 * s, pane.Top + 8 * s);
                }
                else
                {
                    Tag(ViewModel.Painter?.Stats.Align == Engine.AlignMode.Rows ? "Khác biệt - ảnh ghép theo dòng" : "Khác biệt (nền: B)",
                        new SKColor(0xE5, 0x1A, 0x1A), pane.Left + 8 * s, pane.Top + 8 * s);
                }
            }
            if (!ViewModel.HasBothImages)
            {
                DrawHint(canvas, pane, font, "Kéo-thả 2 file ảnh vào đây (hoặc Mở / Dán ở trên) để so sánh");
            }
        }
    }

    private static void DrawHint(SKCanvas canvas, SKRect pane, SKPaint font, string text)
    {
        // Nền trắng mờ bo góc: hướng dẫn vẫn đọc được khi nằm đè lên ảnh còn lại (vd vừa bỏ ảnh A).
        float width = font.MeasureText(text);
        float pad = font.TextSize * 0.8f;
        var box = new SKRect(pane.MidX - width / 2 - pad, pane.MidY - font.TextSize - pad / 2, pane.MidX + width / 2 + pad, pane.MidY + pad);
        using var bg = new SKPaint { Color = SKColors.White.WithAlpha(225), IsAntialias = true };
        canvas.DrawRoundRect(box, pad / 2, pad / 2, bg);
        font.Color = new SKColor(0, 0, 0, 170);
        canvas.DrawText(text, pane.MidX - width / 2, pane.MidY, font);
    }

    private void DrawSwipeDivider(SKCanvas canvas, SKRect pane)
    {
        float s = RasterScale;
        float x = pane.Left + _pan.X + (float)ViewModel.SwipeX * _zoom;
        using var line = new SKPaint { Color = SKColors.White, StrokeWidth = 2 * s, IsAntialias = true };
        using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 90), StrokeWidth = 4 * s, IsAntialias = true };
        canvas.DrawLine(x, pane.Top, x, pane.Bottom, shadow);
        canvas.DrawLine(x, pane.Top, x, pane.Bottom, line);
        using var knob = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var knobBorder = new SKPaint { Color = new SKColor(0, 0, 0, 120), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * s, IsAntialias = true };
        canvas.DrawCircle(x, pane.MidY, 12 * s, knob);
        canvas.DrawCircle(x, pane.MidY, 12 * s, knobBorder);
        using var arrow = new SKPaint { Color = new SKColor(60, 60, 60), StrokeWidth = 1.8f * s, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
        canvas.DrawLine(x - 7 * s, pane.MidY, x - 3 * s, pane.MidY - 4 * s, arrow);
        canvas.DrawLine(x - 7 * s, pane.MidY, x - 3 * s, pane.MidY + 4 * s, arrow);
        canvas.DrawLine(x + 7 * s, pane.MidY, x + 3 * s, pane.MidY - 4 * s, arrow);
        canvas.DrawLine(x + 7 * s, pane.MidY, x + 3 * s, pane.MidY + 4 * s, arrow);
    }
}
