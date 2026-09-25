using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ScreenCapture.Models;
using ScreenCapture.Services;
using ScreenCapture.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using Windows.Foundation;
using WinRT.Interop;

namespace ScreenCapture.Views;

/// <summary>1 mục trong flyout chọn Stamps - Brush dùng cho preview Number stamp (hình tròn màu) và
/// làm màu FontIcon preview của General stamp; Glyph rỗng cho Number stamps (preview vẽ bằng Ellipse
/// trong XAML thay vì icon).</summary>
public sealed class StampPickerItem
{
    public StampKind Kind { get; set; }
    public SolidColorBrush Brush { get; set; } = new(Microsoft.UI.Colors.Black);
    public string Glyph { get; set; } = string.Empty;
    public double Rotation => StampAnnotation.RotationOf(Kind);
}

/// <summary>Owns all pointer/canvas interaction (View responsibility, same split CsvEditor's
/// MainWindow uses) - EditorViewModel itself never references WinUI/Skia UI types beyond SKBitmap.</summary>
public sealed partial class EditorWindow : Window
{
    private static readonly ILogger Log = AppLog.For(nameof(EditorWindow));

    /// <summary>Ảnh (tab) đang hiển thị. Mỗi lần chụp là 1 EditorViewModel riêng (bitmap, shape, lịch sử
    /// Undo riêng) nằm trong Tag của 1 TabViewItem; đổi tab = đổi _viewModel (xem SwitchTo).</summary>
    private EditorViewModel _viewModel = null!;
    private readonly IImageFileService _fileService;
    private readonly IClipboardService _clipboardService;
    private readonly SessionService _session;
    private bool _forceClose;
    private AnnotationShape? _draftShape;
    private SKPoint _dragStartPoint;
    private bool _isCropping;
    private SKRect _cropRect;

    private StampKind? _selectedStampKind;
    private SKColor _selectedStampColor;
    private int _numberStampCounter = 1;

    // Move tool drag state.
    private AnnotationShape? _movingShape;
    private SKRect _movingOldBounds;
    private SKPoint _moveDragStart;
    private int _resizingHandle = -1; // -1 = none, 0..3 = TL/TR/BL/BR
    private int _lineEndpointHandle = -1; // -1 = none, 0 = điểm đầu, 1 = điểm cuối của Line/Arrow

    // Kích thước stamp đặt tiếp theo: nhớ theo stamp vừa được kéo to/nhỏ, về mặc định khi chọn lại
    // stamp từ flyout Stamps (bắt đầu đặt từ đầu).
    private const float DefaultStampSize = 32f;
    private float _stampSize = DefaultStampSize;

    public ObservableCollection<StampPickerItem> NumberStamps { get; } = [];
    public ObservableCollection<StampPickerItem> GeneralStamps { get; } = [];

    // StampsToolButton is a plain Button (Flyout is Button-only in WinUI3, ToggleButton has no
    // Flyout property) - nó không có IsChecked nên không nằm trong danh sách bật/tắt dưới đây.
    private List<ToggleButton> ToolButtons => [
        MoveToolButton, SelectToolButton, RectangleToolButton, EllipseToolButton, LineToolButton, ArrowToolButton,
        PenToolButton, HighlightToolButton, TextToolButton, FillToolButton, MosaicToolButton, BlurToolButton,
    ];

    /// <param name="restored">Các tab của phiên trước (SessionService.Load), có thể rỗng.</param>
    /// <param name="capture">Ảnh vừa chụp để mở thành tab mới, null nếu chỉ mở lại phiên cũ.
    /// Phải có ít nhất 1 trong 2.</param>
    public EditorWindow(IImageFileService fileService, IClipboardService clipboardService, SessionService session,
        IReadOnlyList<SessionDocument> restored, Guid? activeId, SKBitmap? capture)
    {
        InitializeComponent();
        _fileService = fileService;
        _clipboardService = clipboardService;
        _session = session;

        foreach (var doc in restored)
        {
            var vm = new EditorViewModel(doc.Bitmap, fileService, clipboardService, WindowNative.GetWindowHandle(this))
            {
                Id = doc.Id,
                Title = doc.Title,
            };
            vm.RestoreFromSession(doc.Shapes, doc.SavedToFile);
            AddTab(vm);
        }
        if (capture is not null)
        {
            AddCapture(capture);
        }
        else
        {
            var active = DocumentTabs.TabItems.OfType<TabViewItem>().FirstOrDefault(t => (t.Tag as EditorViewModel)?.Id == activeId)
                ?? DocumentTabs.TabItems.OfType<TabViewItem>().Last();
            SwitchTo((EditorViewModel)active.Tag);
            DocumentTabs.SelectedItem = active;
        }

        this.Content.KeyDown += Content_KeyDown;
        // Bấm X trên thanh tiêu đề: lưu tạm phiên làm việc rồi mới đóng (lần mở sau khôi phục lại tab).
        AppWindow.Closing += async (_, args) =>
        {
            if (!_forceClose)
            {
                args.Cancel = true;
                await RequestCloseAsync();
            }
        };

        // Mở full màn hình (maximize) cho dễ chỉnh sửa - ảnh chụp thường lớn hơn kích thước cửa sổ
        // mặc định, ScrollViewer bao Canvas xử lý phần còn lại nếu ảnh vẫn lớn hơn cả màn hình.
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }

        UpdateCanvasLayout();
        // Lúc constructor chạy XamlRoot chưa có → scale tạm lấy theo DPI cửa sổ; tính lại khi đã load
        // và khi cửa sổ bị kéo sang màn hình DPI khác.
        Canvas.Loaded += (_, _) =>
        {
            UpdateCanvasLayout();
            Content.XamlRoot.Changed += (_, _) => UpdateCanvasLayout();
        };

        Color1ColorPicker.Color = ToWindowsColor(_viewModel.StrokeColor);
        Color2ColorPicker.Color = ToWindowsColor(_viewModel.FillColor);
        Color1Swatch.Fill = new SolidColorBrush(Color1ColorPicker.Color);
        Color2Swatch.Fill = new SolidColorBrush(Color2ColorPicker.Color);

        // Minimum/Maximum/Value gán qua XAML attribute từng gây XamlParseException lúc chạy
        // ("Failed to assign to property RangeBase.Minimum") - gán qua code-behind để tránh.
        SizeSlider.Minimum = 1;
        SizeSlider.Maximum = 20;
        SizeSlider.Value = 3;

        // NumberBox cùng họ RangeBase-like control - áp dụng lại bài học Slider ở trên, không set
        // Minimum/SmallChange qua XAML.
        CurrentNumberBox.Minimum = 1;
        CurrentNumberBox.SmallChange = 1;
        NextNumberBox.Minimum = 1;
        NextNumberBox.SmallChange = 1;
        NextNumberBox.Value = _numberStampCounter;

        PopulateStampPickers();
        // Mở ảnh ra ở tool Move (con trỏ) giống PicPick: bấm nhầm không vẽ ra gì, và thấy ngay 8 handle
        // để đổi kích thước khung ảnh.
        SelectTool(CaptureTool.Move, MoveToolButton);
    }

    // ---- Nhiều ảnh chụp dạng tab (giống PicPick) ----

    /// <summary>Thêm 1 ảnh chụp mới thành tab mới và chuyển sang tab đó. CaptureLauncherWindow gọi hàm này
    /// cho các lần chụp sau thay vì mở thêm cửa sổ Editor.</summary>
    public void AddCapture(SKBitmap bitmap)
    {
        var vm = new EditorViewModel(bitmap, _fileService, _clipboardService, WindowNative.GetWindowHandle(this))
        {
            Title = UniqueTitle(DateTime.Now.ToString("yyyy-MM-dd HH mm ss")),
        };
        var tab = AddTab(vm);
        SwitchTo(vm);
        DocumentTabs.SelectedItem = tab;

        // Editor đang thu nhỏ thì mở lại cho người dùng thấy ảnh mới.
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Maximize();
        }

        // Ghi tạm ngay khi có ảnh chụp mới - app bị tắt đột ngột cũng không mất ảnh vừa chụp.
        TrySaveSession();
    }

    /// <summary>Ảnh (tab) đang hiển thị - launcher dùng để tự lưu / tự copy ngay sau khi chụp.</summary>
    public EditorViewModel CurrentDocument => _viewModel;

    /// <summary>Launcher yêu cầu mở cửa sổ Cài đặt (nút "Cài đặt" ở tab Tệp).</summary>
    public event EventHandler? SettingsRequested;

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Ghi lại phiên tạm ngay (sau khi tự lưu ảnh, hoặc đổi tuỳ chọn "nhớ tab" / giới hạn).</summary>
    public void PersistSession() => TrySaveSession();

    private TabViewItem AddTab(EditorViewModel vm)
    {
        var tab = new TabViewItem
        {
            Header = vm.Title,
            Tag = vm,
            IconSource = new FontIconSource { Glyph = "" },
        };
        DocumentTabs.TabItems.Add(tab);
        return tab;
    }

    /// <summary>Ghi các tab đang mở vào thư mục phiên tạm (xem SessionService). Trả false nếu lỗi ghi
    /// (ổ đầy, không có quyền...) - không để lỗi này làm hỏng thao tác của người dùng.</summary>
    private bool TrySaveSession()
    {
        try
        {
            var documents = DocumentTabs.TabItems.OfType<TabViewItem>()
                .Select(t => (EditorViewModel)t.Tag)
                .Select(vm => new SessionDocument(vm.Id, vm.Title, vm.Bitmap, vm.Annotations.ToList(), vm.SavedToFile))
                .ToList();
            _session.Save(documents, _viewModel?.Id);
            return true;
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Không lưu tạm được phiên làm việc");
            if (_viewModel is not null)
            {
                _viewModel.StatusText = $"Không lưu tạm được phiên làm việc: {ex.Message}";
            }
            return false;
        }
    }

    /// <summary>2 lần chụp trong cùng 1 giây → thêm hậu tố (2), (3)... cho tên tab khỏi trùng.</summary>
    private string UniqueTitle(string baseTitle)
    {
        var existing = DocumentTabs.TabItems.OfType<TabViewItem>().Select(t => (t.Tag as EditorViewModel)?.Title).ToHashSet();
        string title = baseTitle;
        for (int i = 2; existing.Contains(title); i++)
        {
            title = $"{baseTitle} ({i})";
        }
        return title;
    }

    private void SwitchTo(EditorViewModel vm)
    {
        if (ReferenceEquals(vm, _viewModel))
        {
            return;
        }

        var previous = _viewModel;
        if (previous is not null)
        {
            previous.RequestRedraw -= ViewModel_RequestRedraw;
            previous.PropertyChanged -= ViewModel_PropertyChanged;
            previous.SelectedAnnotation = null;
            // Công cụ, màu, cỡ nét là thiết lập của cửa sổ, không phải của từng ảnh → mang sang ảnh mới.
            vm.SelectedTool = previous.SelectedTool;
            vm.StrokeColor = previous.StrokeColor;
            vm.FillColor = previous.FillColor;
            vm.StrokeWidth = previous.StrokeWidth;
        }

        _viewModel = vm;
        vm.RequestRedraw += ViewModel_RequestRedraw;
        vm.PropertyChanged += ViewModel_PropertyChanged;

        // Bỏ mọi thao tác kéo dở / vùng chọn của ảnh trước (toạ độ không còn đúng với ảnh mới).
        _draftShape = null;
        _movingShape = null;
        _resizingHandle = -1;
        _lineEndpointHandle = -1;
        _canvasHandle = -1;
        _isDraggingRegion = false;
        _regionHandle = -1;
        SetRegion(null);

        Title = $"ScreenCapture - {vm.Title}";
        StatusText.Text = vm.StatusText;
        UpdateSelectionButtons();
        UpdateNumberStampTab();
        UpdateCanvasLayout();
        CanvasScroller.ChangeView(0, 0, null, true);
    }

    private void ViewModel_RequestRedraw(object? sender, EventArgs e) => Canvas.Invalidate();

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.Bitmap))
        {
            // Ảnh đổi (cắt, xoá vùng, đổi khung, undo...) → toạ độ vùng chọn cũ không còn đúng.
            SetRegion(null);
            UpdateCanvasLayout();
        }
        if (e.PropertyName == nameof(EditorViewModel.StatusText))
        {
            StatusText.Text = _viewModel.StatusText;
        }
        if (e.PropertyName == nameof(EditorViewModel.SelectedAnnotation))
        {
            UpdateSelectionButtons();
            UpdateNumberStampTab();
        }
    }

    private void UpdateSelectionButtons()
    {
        bool hasSelection = _viewModel.SelectedAnnotation is not null;
        DeleteButton.IsEnabled = hasSelection;
        BringToFrontButton.IsEnabled = hasSelection;
        SendToBackButton.IsEnabled = hasSelection;
    }

    private void DocumentTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DocumentTabs.SelectedItem is TabViewItem { Tag: EditorViewModel vm })
        {
            SwitchTo(vm);
        }
    }

    private async void DocumentTabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab is not TabViewItem { Tag: EditorViewModel vm } tab || !await ConfirmCloseDocumentAsync(vm))
        {
            return;
        }

        DocumentTabs.TabItems.Remove(tab);
        // Ghi lại phiên ngay: file tạm của tab vừa đóng bị xoá luôn khỏi ổ đĩa.
        TrySaveSession();
        if (DocumentTabs.TabItems.Count == 0)
        {
            // Đóng tab cuối = đóng Editor (đã hỏi lưu cho tab này rồi; thư mục phiên giờ rỗng).
            _forceClose = true;
            Close();
        }
    }

    private async void CloseAllTabsButton_Click(object sender, RoutedEventArgs e) => await CloseAllTabsAsync();

    /// <summary>"Đóng tất cả": còn ảnh chưa lưu → hỏi Lưu tất cả (chọn 1 thư mục, lưu hết các ảnh chưa
    /// lưu vào đó, tên file = tên tab) / Đóng không lưu / Huỷ. Xong thì đóng mọi tab = đóng Editor,
    /// thư mục phiên tạm được dọn rỗng.</summary>
    private async Task CloseAllTabsAsync()
    {
        var unsaved = DocumentTabs.TabItems.OfType<TabViewItem>()
            .Select(t => (EditorViewModel)t.Tag)
            .Where(vm => vm.NeedsSave)
            .ToList();

        if (unsaved.Count > 0)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Đóng tất cả ảnh",
                Content = $"Có {unsaved.Count} ảnh chưa được lưu. Chọn 1 thư mục để lưu tất cả trước khi đóng?",
                PrimaryButtonText = "Lưu tất cả...",
                SecondaryButtonText = "Đóng không lưu",
                CloseButtonText = "Huỷ",
                DefaultButton = ContentDialogButton.Primary,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.None)
            {
                return;
            }
            if (result == ContentDialogResult.Primary && !await SaveAllToFolderAsync(unsaved))
            {
                return; // huỷ chọn thư mục hoặc lưu lỗi → giữ nguyên các tab
            }
        }

        DocumentTabs.TabItems.Clear();
        TrySaveSession(); // 0 tab → thư mục phiên tạm được dọn rỗng
        _forceClose = true;
        Close();
    }

    /// <summary>Chọn thư mục rồi lưu từng ảnh vào đó. Trả false nếu huỷ chọn thư mục hoặc có ảnh lưu lỗi
    /// (báo lỗi, không đóng gì để khỏi mất ảnh).</summary>
    private async Task<bool> SaveAllToFolderAsync(IReadOnlyList<EditorViewModel> documents)
    {
        var folder = await _fileService.PickFolderAsync(WindowNative.GetWindowHandle(this));
        if (folder is null)
        {
            return false;
        }

        var failed = new List<string>();
        foreach (var vm in documents)
        {
            try
            {
                vm.SaveToFolder(folder);
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Lưu tất cả: không lưu được {Title} vào {Folder}", vm.Title, folder);
                failed.Add($"{vm.Title}: {ex.Message}");
            }
        }

        if (failed.Count > 0)
        {
            await new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Không lưu được một số ảnh",
                Content = string.Join(Environment.NewLine, failed) + Environment.NewLine + "Các tab vẫn được giữ nguyên.",
                CloseButtonText = "OK",
            }.ShowAsync();
            return false;
        }
        return true;
    }

    /// <summary>Ảnh cần lưu → hỏi Lưu / Không lưu / Huỷ. Trả true nếu được phép đóng.</summary>
    private async Task<bool> ConfirmCloseDocumentAsync(EditorViewModel vm)
    {
        if (!vm.NeedsSave)
        {
            return true;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Ảnh chưa được lưu",
            Content = $"Lưu ảnh \"{vm.Title}\" trước khi đóng?",
            PrimaryButtonText = "Lưu",
            SecondaryButtonText = "Không lưu",
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => await vm.SaveToFileAsync(),
            ContentDialogResult.Secondary => true,
            _ => false,
        };
    }

    /// <summary>Đóng cả cửa sổ (nút X hoặc nút Đóng ở tab Tệp): lưu tạm mọi tab vào thư mục phiên để lần
    /// mở sau khôi phục lại - không cần hỏi. Chỉ khi lưu tạm lỗi mà còn ảnh chưa lưu mới hỏi xác nhận.</summary>
    /// Launcher cũng gọi hàm này khi Thoát từ menu khay. Trả false nếu người dùng bấm Huỷ.</summary>
    public async Task<bool> RequestCloseAsync()
    {
        int unsaved = DocumentTabs.TabItems.OfType<TabViewItem>().Count(t => t.Tag is EditorViewModel { NeedsSave: true });
        bool saved = TrySaveSession(); // tắt "nhớ tab" thì lần ghi này chỉ dọn sạch thư mục tạm
        bool persisted = saved && _session.Enabled;
        if (!persisted && unsaved > 0)
        {
            // Thoát từ khay khi Editor đang thu nhỏ → mở lên cho người dùng thấy hộp thoại.
            var hwnd = WindowNative.GetWindowHandle(this);
            if (Services.Interop.NativeMethods.IsIconic(hwnd))
            {
                Services.Interop.NativeMethods.ShowWindow(hwnd, Services.Interop.NativeMethods.SW_RESTORE);
            }
            Activate();

            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = saved ? "Còn ảnh chưa được lưu" : "Không lưu tạm được ảnh",
                Content = saved
                    ? $"Có {unsaved} ảnh chụp chưa lưu sẽ bị mất khi đóng cửa sổ (tuỳ chọn \"Nhớ các tab\" đang tắt). Vẫn đóng?"
                    : $"Không ghi được thư mục tạm ({SessionService.Folder}). Có {unsaved} ảnh chụp chưa lưu sẽ bị mất khi đóng cửa sổ. Vẫn đóng?",
                PrimaryButtonText = "Đóng, không lưu",
                CloseButtonText = "Huỷ",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return false;
            }
        }
        _forceClose = true;
        Close();
        return true;
    }

    private static Windows.UI.Color ToWindowsColor(SKColor c) => Windows.UI.Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue);
    private static SKColor ToSkColor(Windows.UI.Color c) => new(c.R, c.G, c.B, c.A);

    private void PopulateStampPickers()
    {
        var numberBrushes = new[]
        {
            Windows.UI.Color.FromArgb(255, 0xD8, 0x64, 0x45), Colors.RoyalBlue, Colors.DarkOrange, Colors.SeaGreen,
            Colors.MediumPurple, Colors.DeepSkyBlue, Colors.DimGray,
        };
        foreach (var brush in numberBrushes)
        {
            NumberStamps.Add(new StampPickerItem { Kind = StampKind.Number, Brush = new SolidColorBrush(brush) });
        }

        var generalBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xD8, 0x64, 0x45));
        (StampKind Kind, string Glyph)[] general =
        [
            (StampKind.ArrowUp, ""), (StampKind.ArrowUpRight, ""), (StampKind.ArrowRight, ""),
            (StampKind.ArrowDownRight, ""), (StampKind.ArrowDown, ""), (StampKind.ArrowDownLeft, ""),
            (StampKind.ArrowLeft, ""), (StampKind.ArrowUpLeft, ""),
            (StampKind.Bookmark, ""), (StampKind.Pin, ""), (StampKind.Flag, ""), (StampKind.Tag, ""),
            (StampKind.Info, ""), (StampKind.Warning, ""), (StampKind.NoEntry, ""), (StampKind.Heart, ""),
            (StampKind.Plus, ""), (StampKind.Minus, ""), (StampKind.Check, ""), (StampKind.Cross, ""),
            (StampKind.Star, ""),
        ];
        foreach (var (kind, glyph) in general)
        {
            GeneralStamps.Add(new StampPickerItem { Kind = kind, Brush = generalBrush, Glyph = glyph });
        }
    }

    private void Canvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        // Mọi thứ phía dưới vẽ theo toạ độ ảnh; _viewOrigin chừa lề quanh ảnh cho 8 handle khung ảnh
        // (và dịch theo khi đang kéo mở rộng khung sang trái/lên trên).
        canvas.Translate(_viewOrigin.X, _viewOrigin.Y);

        var imageRect = SKRect.Create(_viewModel.Bitmap.Width, _viewModel.Bitmap.Height);
        using (var edgePaint = new SKPaint { Color = new SKColor(0, 0, 0, 70), Style = SKPaintStyle.Stroke, StrokeWidth = 1 })
        {
            canvas.DrawRect(SKRect.Inflate(imageRect, 0.5f, 0.5f), edgePaint);
        }
        canvas.DrawBitmap(_viewModel.Bitmap, 0, 0);
        foreach (var shape in _viewModel.Annotations)
        {
            shape.Render(canvas, _viewModel.Bitmap);
        }
        _draftShape?.Render(canvas, _viewModel.Bitmap);

        if (_viewModel.SelectedAnnotation is { } selected)
        {
            using var handleFill = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
            using var handleStroke = new SKPaint { Color = SKColors.DeepSkyBlue, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };

            if (selected is LineArrowAnnotation line)
            {
                // Đường/mũi tên: chỉ 2 handle tròn ở 2 đầu (kéo để đổi hướng/độ dài), không vẽ khung bao.
                foreach (var p in new[] { line.Start, line.End })
                {
                    canvas.DrawCircle(p, HandleSize / 2 + 1, handleFill);
                    canvas.DrawCircle(p, HandleSize / 2 + 1, handleStroke);
                }
            }
            else
            {
                var bounds = selected.NormalizedBounds;
                using var selectPaint = new SKPaint
                {
                    Color = SKColors.DeepSkyBlue,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 2,
                    PathEffect = SKPathEffect.CreateDash([6, 4], 0),
                };
                canvas.DrawRect(bounds, selectPaint);

                (float X, float Y)[] corners =
                [
                    (bounds.Left, bounds.Top), (bounds.Right, bounds.Top),
                    (bounds.Left, bounds.Bottom), (bounds.Right, bounds.Bottom),
                ];
                foreach (var (x, y) in corners)
                {
                    canvas.DrawRect(new SKRect(x - HandleSize / 2, y - HandleSize / 2, x + HandleSize / 2, y + HandleSize / 2), handleFill);
                    canvas.DrawRect(new SKRect(x - HandleSize / 2, y - HandleSize / 2, x + HandleSize / 2, y + HandleSize / 2), handleStroke);
                }
            }
        }

        if (_isCropping)
        {
            using var paint = new SKPaint { Color = SKColors.DeepSkyBlue, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
            canvas.DrawRect(_cropRect, paint);
        }

        if (_isDraggingRegion || _region is not null)
        {
            var r = _isDraggingRegion || _regionHandle >= 0 ? _regionDrag : ToRect(_region!.Value);
            // Viền "kiến bò" kiểu PicPick/Paint: nét trắng liền + nét đen đứt đè lên, thấy được trên mọi nền.
            using var white = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            using var black = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1,
                PathEffect = SKPathEffect.CreateDash([4, 4], 0),
            };
            var outline = SKRect.Inflate(r, 0.5f, 0.5f);
            canvas.DrawRect(outline, white);
            canvas.DrawRect(outline, black);

            if (!_isDraggingRegion)
            {
                // 8 handle chỉnh kích thước vùng chọn (kéo bên trong vùng = di chuyển).
                using var handleFill = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
                using var handleStroke = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
                foreach (var p in CanvasHandlePoints(r))
                {
                    var h = new SKRect(p.X - HandleSize / 2, p.Y - HandleSize / 2, p.X + HandleSize / 2, p.Y + HandleSize / 2);
                    canvas.DrawRect(h, handleFill);
                    canvas.DrawRect(h, handleStroke);
                }
            }
        }

        if (ShowCanvasHandles)
        {
            var frame = _canvasHandle >= 0 ? _canvasResizeRect : imageRect;
            if (_canvasHandle >= 0)
            {
                using var previewPaint = new SKPaint
                {
                    Color = SKColors.DeepSkyBlue,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1.5f,
                    PathEffect = SKPathEffect.CreateDash([6, 4], 0),
                };
                canvas.DrawRect(frame, previewPaint);
            }
            using var fill = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
            using var stroke = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            foreach (var p in CanvasHandlePoints(frame))
            {
                var r = new SKRect(p.X - HandleSize / 2, p.Y - HandleSize / 2, p.X + HandleSize / 2, p.Y + HandleSize / 2);
                canvas.DrawRect(r, fill);
                canvas.DrawRect(r, stroke);
            }
        }
    }

    /// <summary>Pointer events trả toạ độ logic (DIP), nhưng SKXamlCanvas vẽ theo pixel vật lý. Trên máy
    /// DPI scale khác 100% (rất phổ biến khi dùng nhiều màn hình), 2 hệ toạ độ này lệch nhau đúng bằng
    /// RasterizationScale - không nhân lại thì click sẽ vẽ/hit-test sai vị trí (đã gặp thực tế: stamp
    /// không nằm đúng chỗ click). Nhân theo scale rồi trừ _viewOrigin để ra toạ độ pixel trên ảnh.</summary>
    private SKPoint ToCanvasPoint(Point p)
    {
        double scale = CurrentScale;
        return new SKPoint((float)(p.X * scale) - _viewOrigin.X, (float)(p.Y * scale) - _viewOrigin.Y);
    }

    private double CurrentScale =>
        Content.XamlRoot?.RasterizationScale
        ?? Services.Interop.NativeMethods.GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;

    // ---- Kéo 8 handle quanh ảnh (tool Move, không chọn shape nào) để đổi kích thước khung ảnh ----

    /// <summary>Lề (pixel) quanh ảnh trên canvas để handle khung ảnh không bị cắt mất.</summary>
    private const float CanvasPad = 16f;
    private SKPoint _viewOrigin = new(CanvasPad, CanvasPad);
    private int _canvasHandle = -1; // -1 = none, 0..7 = TL, T, TR, R, BR, B, BL, L
    private SKRect _canvasResizeRect;
    private Point _canvasDragStartWindow;

    // ---- Tool Select: vùng chọn chữ nhật trên ảnh (toạ độ pixel ảnh) ----

    private SKRectI? _region;
    private bool _isDraggingRegion;
    private SKRect _regionDrag;
    private const int RegionMoveHandle = 8;
    private int _regionHandle = -1; // -1 = none, 0..7 = handle (thứ tự như CanvasHandlePoints), 8 = di chuyển
    private SKRectI _regionEditStart;

    private static SKRect ToRect(SKRectI r) => SKRect.Create(r.Left, r.Top, r.Width, r.Height);

    /// <summary>Đặt/bỏ vùng chọn và hiện/ẩn tab contextual "Vùng chọn" tương ứng.</summary>
    private void SetRegion(SKRectI? region)
    {
        _region = region;
        if (region is { } r)
        {
            RegionTabHeader.Visibility = Visibility.Visible;
            SelectRibbonTab(RibbonTab.Region);
            StatusText.Text = $"Vùng chọn: {r.Width} × {r.Height} px";
        }
        else
        {
            RegionTabHeader.Visibility = Visibility.Collapsed;
            if (RegionTabHeader.IsChecked == true)
            {
                SelectRibbonTab(RibbonTab.Home);
            }
        }
        Canvas.Invalidate();
    }

    private void PasteButton_Click(object sender, RoutedEventArgs e) => PasteFromClipboard();

    /// <summary>Dán ảnh từ clipboard: đặt ở góc trên-trái vùng chọn (nếu có), không thì ở góc trên-trái
    /// phần ảnh đang nhìn thấy. Sau khi dán chuyển sang Move và chọn sẵn ảnh để kéo/co giãn ngay.</summary>
    private async void PasteFromClipboard()
    {
        SKPoint topLeft;
        if (_region is { } r)
        {
            topLeft = new SKPoint(r.Left, r.Top);
        }
        else
        {
            double scale = CurrentScale;
            topLeft = new SKPoint(
                MathF.Round(Math.Max(0f, (float)(CanvasScroller.HorizontalOffset * scale) - _viewOrigin.X)),
                MathF.Round(Math.Max(0f, (float)(CanvasScroller.VerticalOffset * scale) - _viewOrigin.Y)));
        }

        try
        {
            if (await _viewModel.PasteFromClipboardAsync(topLeft) is { } pasted)
            {
                SelectTool(CaptureTool.Move, MoveToolButton);
                _viewModel.SelectedAnnotation = pasted;
                Canvas.Invalidate();
            }
        }
        catch (Exception ex)
        {
            // Clipboard WinRT có thể lỗi ở app unpackaged / định dạng ảnh lạ - báo thay vì crash.
            Log.LogError(ex, "Không dán được ảnh từ clipboard");
            _viewModel.StatusText = $"Không dán được ảnh: {ex.Message}";
        }
    }

    private void RegionCropButton_Click(object sender, RoutedEventArgs e) => CropToRegion();
    private void RegionCopyButton_Click(object sender, RoutedEventArgs e) => CopyRegion();
    private void RegionCutButton_Click(object sender, RoutedEventArgs e) => CutRegion();
    private void RegionEraseButton_Click(object sender, RoutedEventArgs e) => EraseRegion();
    private void RegionClearButton_Click(object sender, RoutedEventArgs e) => SetRegion(null);

    private void CropToRegion()
    {
        if (_region is { } r)
        {
            _viewModel.Crop(SKRect.Create(r.Left, r.Top, r.Width, r.Height)); // đổi Bitmap → tự bỏ vùng chọn
        }
    }

    private async void CopyRegion()
    {
        if (_region is { } r)
        {
            await _viewModel.CopyRegionAsync(r);
        }
    }

    private async void CutRegion()
    {
        if (_region is { } r)
        {
            await _viewModel.CopyRegionAsync(r);
            _viewModel.EraseRegion(r);
            _viewModel.StatusText = $"Đã cut vùng {r.Width} × {r.Height} px vào clipboard.";
        }
    }

    private void EraseRegion()
    {
        if (_region is { } r)
        {
            _viewModel.EraseRegion(r);
        }
    }

    private bool ShowCanvasHandles =>
        _viewModel.SelectedTool == CaptureTool.Move && _viewModel.SelectedAnnotation is null && !_isCropping;

    private static SKPoint[] CanvasHandlePoints(SKRect r) =>
    [
        new(r.Left, r.Top), new(r.MidX, r.Top), new(r.Right, r.Top), new(r.Right, r.MidY),
        new(r.Right, r.Bottom), new(r.MidX, r.Bottom), new(r.Left, r.Bottom), new(r.Left, r.MidY),
    ];

    private int? HitTestCanvasHandle(SKPoint pos) =>
        HitTestPoints(CanvasHandlePoints(SKRect.Create(_viewModel.Bitmap.Width, _viewModel.Bitmap.Height)), pos);

    private static int? HitTestPoints(SKPoint[] points, SKPoint pos)
    {
        for (int i = 0; i < points.Length; i++)
        {
            if (Math.Abs(pos.X - points[i].X) <= HandleSize && Math.Abs(pos.Y - points[i].Y) <= HandleSize)
            {
                return i;
            }
        }
        return null;
    }

    /// <summary>Đặt kích thước SKXamlCanvas vừa đủ chứa ảnh (hoặc khung đang kéo, nếu lớn hơn) + lề, và
    /// dời _viewOrigin khi khung vượt sang trái/lên trên toạ độ 0 của ảnh.</summary>
    private void UpdateCanvasLayout(SKRect? preview = null)
    {
        var content = SKRect.Create(_viewModel.Bitmap.Width, _viewModel.Bitmap.Height);
        if (preview is { } p)
        {
            content = SKRect.Union(content, p);
        }
        _viewOrigin = new SKPoint(CanvasPad - content.Left, CanvasPad - content.Top);
        double scale = CurrentScale;
        Canvas.Width = (content.Width + 2 * CanvasPad) / scale;
        Canvas.Height = (content.Height + 2 * CanvasPad) / scale;
        Canvas.Invalidate();
    }

    private const float HandleSize = 8f;

    /// <summary>Trả về chỉ số handle (0=TL,1=TR,2=BL,3=BR) nếu pos rơi vào 1 trong 4 góc của bounds,
    /// null nếu không trúng handle nào.</summary>
    private static int? HitTestHandle(SKRect bounds, SKPoint pos)
    {
        (float X, float Y)[] corners =
        [
            (bounds.Left, bounds.Top), (bounds.Right, bounds.Top),
            (bounds.Left, bounds.Bottom), (bounds.Right, bounds.Bottom),
        ];
        for (int i = 0; i < corners.Length; i++)
        {
            if (Math.Abs(pos.X - corners[i].X) <= HandleSize && Math.Abs(pos.Y - corners[i].Y) <= HandleSize)
            {
                return i;
            }
        }
        return null;
    }

    /// <summary>0 = điểm đầu, 1 = điểm cuối (đầu mũi tên), null = trúng khúc giữa (di chuyển cả đường).
    /// Vùng bắt mỗi đầu là 1/3 độ dài (tối đa 30px, tối thiểu bằng handle) - nên chỉ cần bấm "gần"
    /// đầu mũi tên rồi kéo là đổi hướng được, không phải nhắm trúng handle nhỏ.</summary>
    private static int? HitTestLineEndpoint(LineArrowAnnotation line, SKPoint pos)
    {
        float radius = Math.Max(HandleSize, Math.Min(30f, line.Length / 3f));
        float toStart = SKPoint.Distance(pos, line.Start);
        float toEnd = SKPoint.Distance(pos, line.End);
        if (Math.Min(toStart, toEnd) > radius)
        {
            return null;
        }
        return toEnd <= toStart ? 1 : 0;
    }

    /// <summary>Bấm trúng handle của shape đang chọn, hoặc trúng 1 shape bất kỳ (shape vẽ sau nằm trên,
    /// nên duyệt ngược) → chọn shape đó và bắt đầu kéo (đầu mút / handle góc / di chuyển cả shape).
    /// Trả false nếu bấm vào vùng trống.</summary>
    private bool TryBeginEditExisting(SKPoint pos, PointerRoutedEventArgs e)
    {
        var selected = _viewModel.SelectedAnnotation;
        if (selected is LineArrowAnnotation selectedLine && HitTestLineEndpoint(selectedLine, pos) is { } selectedEndpoint)
        {
            BeginLineEndpointDrag(selectedLine, selectedEndpoint, e);
            return true;
        }
        if (selected is not null and not LineArrowAnnotation && HitTestHandle(selected.NormalizedBounds, pos) is { } handleIndex)
        {
            _resizingHandle = handleIndex;
            _movingShape = selected;
            _movingOldBounds = selected.NormalizedBounds;
            Canvas.CapturePointer(e.Pointer);
            return true;
        }

        var hit = _viewModel.Annotations.Reverse().FirstOrDefault(s => s.HitTest(pos, 6));
        if (hit is null)
        {
            return false;
        }

        _viewModel.SelectedAnnotation = hit;
        if (hit is LineArrowAnnotation hitLine && HitTestLineEndpoint(hitLine, pos) is { } hitEndpoint)
        {
            // Chưa chọn cũng kéo đầu mũi tên được ngay trong 1 thao tác.
            BeginLineEndpointDrag(hitLine, hitEndpoint, e);
        }
        else
        {
            _movingShape = hit;
            _movingOldBounds = hit.Bounds;
            _moveDragStart = pos;
            Canvas.CapturePointer(e.Pointer);
        }
        Canvas.Invalidate();
        return true;
    }

    private void BeginLineEndpointDrag(LineArrowAnnotation line, int endpoint, PointerRoutedEventArgs e)
    {
        _lineEndpointHandle = endpoint;
        _movingShape = line;
        _movingOldBounds = line.Bounds;
        Canvas.CapturePointer(e.Pointer);
    }

    private static SKRect ResizeFromHandle(SKRect original, int handleIndex, SKPoint pos)
    {
        float left = original.Left, top = original.Top, right = original.Right, bottom = original.Bottom;
        switch (handleIndex)
        {
            case 0: left = pos.X; top = pos.Y; break;
            case 1: right = pos.X; top = pos.Y; break;
            case 2: left = pos.X; bottom = pos.Y; break;
            case 3: right = pos.X; bottom = pos.Y; break;
        }
        return new SKRect(Math.Min(left, right), Math.Min(top, bottom), Math.Max(left, right), Math.Max(top, bottom));
    }

    private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pos = ToCanvasPoint(e.GetCurrentPoint(Canvas).Position);
        _dragStartPoint = pos;

        if (_isCropping)
        {
            _cropRect = new SKRect(pos.X, pos.Y, pos.X, pos.Y);
            Canvas.CapturePointer(e.Pointer);
            return;
        }

        if (_viewModel.SelectedTool == CaptureTool.Select)
        {
            // Đã có vùng chọn: bấm trúng 1 trong 8 handle → chỉnh kích thước, bấm bên trong → di chuyển.
            if (_region is { } current)
            {
                var currentRect = ToRect(current);
                _regionHandle = HitTestPoints(CanvasHandlePoints(currentRect), pos)
                    ?? (currentRect.Contains(pos) ? RegionMoveHandle : -1);
                if (_regionHandle >= 0)
                {
                    _regionEditStart = current;
                    _regionDrag = currentRect;
                    Canvas.CapturePointer(e.Pointer);
                    return;
                }
            }

            // Bấm ngoài vùng: bấm-kéo = chọn vùng mới, bấm không kéo = bỏ chọn.
            SetRegion(null);
            _isDraggingRegion = true;
            _regionDrag = new SKRect(pos.X, pos.Y, pos.X, pos.Y);
            Canvas.CapturePointer(e.Pointer);
            Canvas.Invalidate();
            return;
        }

        if (ShowCanvasHandles && HitTestCanvasHandle(pos) is { } canvasHandle)
        {
            _canvasHandle = canvasHandle;
            _canvasResizeRect = SKRect.Create(_viewModel.Bitmap.Width, _viewModel.Bitmap.Height);
            // Lấy toạ độ theo cửa sổ, không theo Canvas: khi kéo mở rộng sang trái/lên trên, Canvas tự
            // lớn ra và dời ảnh → toạ độ theo Canvas sẽ nhảy theo, gây giật.
            _canvasDragStartWindow = e.GetCurrentPoint(null).Position;
            Canvas.CapturePointer(e.Pointer);
            return;
        }

        // Tô màu thao tác trên pixel ảnh, không chọn shape.
        if (_viewModel.SelectedTool == CaptureTool.Fill)
        {
            _viewModel.SelectedAnnotation = null;
            _viewModel.FloodFill(new SKPointI((int)pos.X, (int)pos.Y));
            return;
        }

        // Với MỌI công cụ: bấm trúng handle / shape có sẵn thì chọn + kéo shape đó (không cần chuyển
        // sang "Di chuyển" trước). Chỉ khi bấm vào vùng trống mới bỏ chọn và dùng công cụ hiện tại.
        // Riêng Bút: luôn vẽ nét mới (viết / khoanh chồng lên hình khác là bình thường) - chỉnh nét đã vẽ
        // bằng công cụ Di chuyển.
        if (_viewModel.SelectedTool != CaptureTool.Pen && TryBeginEditExisting(pos, e))
        {
            return;
        }

        bool hadSelection = _viewModel.SelectedAnnotation is not null;
        _viewModel.SelectedAnnotation = null;
        Canvas.Invalidate();

        switch (_viewModel.SelectedTool)
        {
            case CaptureTool.Rectangle:
                _draftShape = new RectangleAnnotation { Bounds = new SKRect(pos.X, pos.Y, pos.X, pos.Y), Color = _viewModel.StrokeColor, StrokeWidth = _viewModel.StrokeWidth };
                Canvas.CapturePointer(e.Pointer);
                break;
            case CaptureTool.Ellipse:
                _draftShape = new EllipseAnnotation { Bounds = new SKRect(pos.X, pos.Y, pos.X, pos.Y), Color = _viewModel.StrokeColor, StrokeWidth = _viewModel.StrokeWidth };
                Canvas.CapturePointer(e.Pointer);
                break;
            case CaptureTool.Line:
                _draftShape = new LineArrowAnnotation { Bounds = new SKRect(pos.X, pos.Y, pos.X, pos.Y), Color = _viewModel.StrokeColor, StrokeWidth = _viewModel.StrokeWidth, IsArrow = false };
                Canvas.CapturePointer(e.Pointer);
                break;
            case CaptureTool.Arrow:
                _draftShape = new LineArrowAnnotation { Bounds = new SKRect(pos.X, pos.Y, pos.X, pos.Y), Color = _viewModel.StrokeColor, StrokeWidth = _viewModel.StrokeWidth };
                Canvas.CapturePointer(e.Pointer);
                break;
            case CaptureTool.Pen:
                var stroke = new FreehandAnnotation { Color = _viewModel.StrokeColor, StrokeWidth = _viewModel.StrokeWidth };
                stroke.AddPoint(pos);
                _draftShape = stroke;
                Canvas.CapturePointer(e.Pointer);
                break;
            case CaptureTool.Highlight:
                _draftShape = new HighlightAnnotation { Bounds = new SKRect(pos.X, pos.Y, pos.X, pos.Y), Color = _viewModel.FillColor.WithAlpha(90) };
                Canvas.CapturePointer(e.Pointer);
                break;
            case CaptureTool.Mosaic or CaptureTool.Blur:
                _draftShape = new RedactAnnotation
                {
                    Mode = _viewModel.SelectedTool == CaptureTool.Mosaic ? RedactMode.Mosaic : RedactMode.Blur,
                    Bounds = new SKRect(pos.X, pos.Y, pos.X, pos.Y),
                    StrokeWidth = _viewModel.StrokeWidth,
                };
                Canvas.CapturePointer(e.Pointer);
                break;
            case CaptureTool.Text:
                // Đang chọn shape mà bấm ra vùng trống = chỉ bỏ chọn, không bật hộp nhập text ngoài ý muốn.
                if (!hadSelection)
                {
                    PromptForText(pos);
                }
                break;
            case CaptureTool.Stamp:
                // Giống Text: đang chọn stamp (vừa đặt) mà bấm vùng trống = chỉ thoát chỉnh sửa; lần bấm
                // tiếp theo mới đặt stamp kế tiếp (1 → bấm ra ngoài → 2 → ...). Công cụ Stamp vẫn giữ
                // nguyên cho tới khi chọn công cụ khác.
                if (!hadSelection && _selectedStampKind is { } kind)
                {
                    float half = _stampSize / 2;
                    var shape = new StampAnnotation
                    {
                        Kind = kind,
                        Color = _selectedStampColor,
                        // Number stamps: số tự tăng dần mỗi lần đặt (giống PicPick), không cố định.
                        NumberValue = kind == StampKind.Number ? _numberStampCounter++ : 0,
                        Bounds = new SKRect(pos.X - half, pos.Y - half, pos.X + half, pos.Y + half),
                    };
                    _viewModel.AddAnnotation(shape);
                    _viewModel.SelectedAnnotation = shape;
                    if (kind == StampKind.Number)
                    {
                        NextNumberBox.Value = _numberStampCounter;
                    }
                }
                break;
        }
    }

    private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pos = ToCanvasPoint(e.GetCurrentPoint(Canvas).Position);
        bool shift = e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift);

        if (_isCropping && e.GetCurrentPoint(Canvas).Properties.IsLeftButtonPressed)
        {
            _cropRect = MakeRect(_dragStartPoint, pos);
            Canvas.Invalidate();
            return;
        }

        if (_regionHandle >= 0)
        {
            var start = ToRect(_regionEditStart);
            var imageRect = SKRect.Create(_viewModel.Bitmap.Width, _viewModel.Bitmap.Height);
            float dx = MathF.Round(pos.X - _dragStartPoint.X), dy = MathF.Round(pos.Y - _dragStartPoint.Y);
            if (_regionHandle == RegionMoveHandle)
            {
                // Di chuyển nguyên khung, không cho trượt ra ngoài ảnh.
                dx = Math.Clamp(dx, -start.Left, imageRect.Right - start.Right);
                dy = Math.Clamp(dy, -start.Top, imageRect.Bottom - start.Bottom);
                _regionDrag = new SKRect(start.Left + dx, start.Top + dy, start.Right + dx, start.Bottom + dy);
            }
            else
            {
                float left = start.Left, top = start.Top, right = start.Right, bottom = start.Bottom;
                if (_regionHandle is 0 or 6 or 7) left += dx;
                if (_regionHandle is 2 or 3 or 4) right += dx;
                if (_regionHandle is 0 or 1 or 2) top += dy;
                if (_regionHandle is 4 or 5 or 6) bottom += dy;
                _regionDrag = SKRect.Intersect(MakeRect(new SKPoint(left, top), new SKPoint(right, bottom)), imageRect);
            }
            StatusText.Text = $"Vùng chọn: {(int)_regionDrag.Width} × {(int)_regionDrag.Height} px";
            Canvas.Invalidate();
            return;
        }

        if (_isDraggingRegion)
        {
            var end = shift ? SnapToSquare(_dragStartPoint, pos) : pos;
            var imageRect = SKRect.Create(_viewModel.Bitmap.Width, _viewModel.Bitmap.Height);
            _regionDrag = SKRect.Intersect(MakeRect(_dragStartPoint, end), imageRect);
            StatusText.Text = $"Vùng chọn: {(int)_regionDrag.Width} × {(int)_regionDrag.Height} px";
            Canvas.Invalidate();
            return;
        }

        if (_canvasHandle >= 0)
        {
            var now = e.GetCurrentPoint(null).Position;
            double scale = CurrentScale;
            float dx = (float)Math.Round((now.X - _canvasDragStartWindow.X) * scale);
            float dy = (float)Math.Round((now.Y - _canvasDragStartWindow.Y) * scale);
            float left = 0, top = 0, right = _viewModel.Bitmap.Width, bottom = _viewModel.Bitmap.Height;
            if (_canvasHandle is 0 or 6 or 7) left = Math.Min(left + dx, right - 1);
            if (_canvasHandle is 2 or 3 or 4) right = Math.Max(right + dx, left + 1);
            if (_canvasHandle is 0 or 1 or 2) top = Math.Min(top + dy, bottom - 1);
            if (_canvasHandle is 4 or 5 or 6) bottom = Math.Max(bottom + dy, top + 1);
            _canvasResizeRect = new SKRect(left, top, right, bottom);
            UpdateCanvasLayout(_canvasResizeRect);
            StatusText.Text = $"Kích thước: {(int)_canvasResizeRect.Width} × {(int)_canvasResizeRect.Height} px";
            return;
        }

        if (_movingShape is not null && _lineEndpointHandle >= 0)
        {
            var old = _movingOldBounds;
            // Giữ Shift: khoá góc tính từ đầu còn lại (đầu đang đứng yên).
            if (_lineEndpointHandle == 0)
            {
                var anchor = new SKPoint(old.Right, old.Bottom);
                _movingShape.Bounds = LineArrowAnnotation.FromPoints(shift ? SnapToAngle(anchor, pos) : pos, anchor);
            }
            else
            {
                var anchor = new SKPoint(old.Left, old.Top);
                _movingShape.Bounds = LineArrowAnnotation.FromPoints(anchor, shift ? SnapToAngle(anchor, pos) : pos);
            }
            Canvas.Invalidate();
            return;
        }

        if (_movingShape is not null && _resizingHandle >= 0)
        {
            if (_movingShape is StampAnnotation || (shift && _movingShape is RectangleAnnotation or EllipseAnnotation))
            {
                // Giữ Shift: giữ tỉ lệ vuông/tròn, neo ở góc đối diện handle đang kéo (0↔3, 1↔2).
                // Stamp luôn giữ vuông (vẽ theo cạnh ngắn hơn, khung méo chỉ làm handle lệch khỏi hình).
                var o = _movingOldBounds;
                SKPoint[] corners = [new(o.Left, o.Top), new(o.Right, o.Top), new(o.Left, o.Bottom), new(o.Right, o.Bottom)];
                var anchor = corners[3 - _resizingHandle];
                _movingShape.Bounds = MakeRect(anchor, SnapToSquare(anchor, pos));
            }
            else if (shift && _movingShape is ImageAnnotation image)
            {
                // Ảnh dán: giữ Shift để co giãn đúng tỉ lệ gốc, không méo.
                var o = _movingOldBounds;
                SKPoint[] corners = [new(o.Left, o.Top), new(o.Right, o.Top), new(o.Left, o.Bottom), new(o.Right, o.Bottom)];
                var anchor = corners[3 - _resizingHandle];
                _movingShape.Bounds = MakeRect(anchor, SnapToAspect(anchor, pos, image.AspectRatio));
            }
            else
            {
                _movingShape.Bounds = ResizeFromHandle(_movingOldBounds, _resizingHandle, pos);
            }
            Canvas.Invalidate();
            return;
        }

        if (_movingShape is not null)
        {
            float dx = pos.X - _moveDragStart.X;
            float dy = pos.Y - _moveDragStart.Y;
            _movingShape.Bounds = new SKRect(
                _movingOldBounds.Left + dx, _movingOldBounds.Top + dy,
                _movingOldBounds.Right + dx, _movingOldBounds.Bottom + dy);
            Canvas.Invalidate();
            return;
        }

        if (_draftShape is FreehandAnnotation freehand)
        {
            freehand.AddPoint(pos);
            Canvas.Invalidate();
            return;
        }
        if (_draftShape is null)
        {
            return;
        }
        // Line/Arrow giữ nguyên điểm bắt đầu -> điểm hiện tại (không chuẩn hoá) để mũi tên chỉ đúng hướng kéo.
        // Giữ Shift: Line/Arrow khoá góc bội số 45°, Chữ nhật/Elip thành hình vuông/tròn.
        _draftShape.Bounds = _draftShape switch
        {
            LineArrowAnnotation => LineArrowAnnotation.FromPoints(_dragStartPoint, shift ? SnapToAngle(_dragStartPoint, pos) : pos),
            RectangleAnnotation or EllipseAnnotation when shift => MakeRect(_dragStartPoint, SnapToSquare(_dragStartPoint, pos)),
            _ => MakeRect(_dragStartPoint, pos),
        };
        Canvas.Invalidate();
    }

    /// <summary>Bắt hướng anchor→pos về bội số 45° gần nhất (8 hướng). Độ dài lấy theo hình chiếu
    /// của chuột lên hướng đó (giống PowerPoint) nên đầu mút bám sát con trỏ.</summary>
    private static SKPoint SnapToAngle(SKPoint anchor, SKPoint pos)
    {
        float dx = pos.X - anchor.X, dy = pos.Y - anchor.Y;
        const float step = MathF.PI / 4;
        float angle = MathF.Round(MathF.Atan2(dy, dx) / step) * step;
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        float length = dx * cos + dy * sin;
        return new SKPoint(anchor.X + length * cos, anchor.Y + length * sin);
    }

    /// <summary>Điểm đối diện anchor sao cho khung có tỉ lệ rộng/cao = <paramref name="ratio"/>, lấy theo
    /// chiều đang kéo "trội" hơn, giữ hướng kéo.</summary>
    private static SKPoint SnapToAspect(SKPoint anchor, SKPoint pos, float ratio)
    {
        float dx = pos.X - anchor.X, dy = pos.Y - anchor.Y;
        float w = Math.Abs(dx), h = Math.Abs(dy);
        if (w / Math.Max(h, 1f) > ratio)
        {
            w = h * ratio;
        }
        else
        {
            h = w / ratio;
        }
        return new SKPoint(anchor.X + (dx < 0 ? -w : w), anchor.Y + (dy < 0 ? -h : h));
    }

    /// <summary>Điểm đối diện anchor sao cho khung là hình vuông (cạnh = chiều dài hơn), giữ hướng kéo.</summary>
    private static SKPoint SnapToSquare(SKPoint anchor, SKPoint pos)
    {
        float dx = pos.X - anchor.X, dy = pos.Y - anchor.Y;
        float side = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return new SKPoint(anchor.X + (dx < 0 ? -side : side), anchor.Y + (dy < 0 ? -side : side));
    }

    private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        Canvas.ReleasePointerCapture(e.Pointer);

        if (_isDraggingRegion || _regionHandle >= 0)
        {
            var r = _regionDrag;
            var region = new SKRectI((int)MathF.Round(r.Left), (int)MathF.Round(r.Top), (int)MathF.Round(r.Right), (int)MathF.Round(r.Bottom));
            bool valid = region.Width >= 2 && region.Height >= 2;
            // Chỉnh vùng cũ mà thu quá nhỏ → giữ vùng cũ thay vì mất vùng chọn.
            SetRegion(valid ? region : _regionHandle >= 0 ? _regionEditStart : null);
            _isDraggingRegion = false;
            _regionHandle = -1;
            return;
        }

        if (_canvasHandle >= 0)
        {
            _canvasHandle = -1;
            var r = _canvasResizeRect;
            var newRect = new SKRectI((int)r.Left, (int)r.Top, (int)r.Right, (int)r.Bottom);
            if (newRect != new SKRectI(0, 0, _viewModel.Bitmap.Width, _viewModel.Bitmap.Height))
            {
                _viewModel.ResizeCanvas(newRect); // đổi Bitmap → PropertyChanged → UpdateCanvasLayout()
                StatusText.Text = $"Kích thước ảnh: {newRect.Width} × {newRect.Height} px";
            }
            UpdateCanvasLayout();
            return;
        }

        if (_isCropping)
        {
            _isCropping = false;
            CropToolButton.IsChecked = false;
            if (_cropRect.Width > 2 && _cropRect.Height > 2)
            {
                _viewModel.Crop(_cropRect);
            }
            Canvas.Invalidate();
            return;
        }

        if (_movingShape is not null)
        {
            var newBounds = _movingShape.Bounds;
            _movingShape.Bounds = _movingOldBounds; // MoveResizeAnnotation's Execute() re-applies newBounds
            // Chỉ bấm để chọn (không kéo) thì không ghi command rỗng vào lịch sử Undo.
            if (newBounds != _movingOldBounds)
            {
                _viewModel.MoveResizeAnnotation(_movingShape, _movingOldBounds, newBounds);

                if (_resizingHandle >= 0 && _movingShape is StampAnnotation)
                {
                    _stampSize = Math.Max(8f, newBounds.Standardized.Width);
                }
            }
            _movingShape = null;
            _resizingHandle = -1;
            _lineEndpointHandle = -1;
            Canvas.Invalidate();
            return;
        }

        if (_draftShape is null)
        {
            return;
        }

        if (_draftShape.NormalizedBounds.Width > 2 || _draftShape.NormalizedBounds.Height > 2 || _draftShape is FreehandAnnotation)
        {
            _viewModel.AddAnnotation(_draftShape);
            // Vẽ xong là ở chế độ chỉnh sửa luôn (handle + tab contextual nếu có), bấm vùng trống mới thoát.
            // Bút thì không: vẽ liền nhiều nét, handle của nét trước chỉ gây rối.
            if (_draftShape is not FreehandAnnotation)
            {
                _viewModel.SelectedAnnotation = _draftShape;
            }
        }
        _draftShape = null;
        Canvas.Invalidate();
    }

    private static SKRect MakeRect(SKPoint a, SKPoint b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    private async void PromptForText(SKPoint position)
    {
        var textBox = new TextBox { PlaceholderText = "Nhập text...", Width = 240 };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Thêm text",
            Content = textBox,
            PrimaryButtonText = "Thêm",
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(textBox.Text))
        {
            var shape = new TextAnnotation
            {
                Bounds = new SKRect(position.X, position.Y, position.X + 200, position.Y + 30),
                Text = textBox.Text,
                Color = _viewModel.StrokeColor,
            };
            _viewModel.AddAnnotation(shape);
            _viewModel.SelectedAnnotation = shape;
            Canvas.Invalidate();
        }
    }

    /// <summary>Phím tắt của Editor. Chỉ nhận phím mà control đang focus chưa xử lý (TextBox trong
    /// NumberBox tự xử lý Ctrl+Z/Ctrl+C/Backspace của nó), nên không cướp phím khi đang gõ số.</summary>
    private void Content_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = IsKeyDown(Windows.System.VirtualKey.Control);
        bool shift = IsKeyDown(Windows.System.VirtualKey.Shift);

        // Đang có vùng chọn (tool Select): Ctrl+C/Ctrl+X/Delete/Enter/Esc tác động lên vùng đó.
        if (_region is not null)
        {
            Action? regionAction = (ctrl, e.Key) switch
            {
                (true, Windows.System.VirtualKey.C) => CopyRegion,
                (true, Windows.System.VirtualKey.X) => CutRegion,
                (false, Windows.System.VirtualKey.Delete or Windows.System.VirtualKey.Back) => EraseRegion,
                (false, Windows.System.VirtualKey.Enter) => CropToRegion,
                (false, Windows.System.VirtualKey.Escape) => () => SetRegion(null),
                _ => null,
            };
            if (regionAction is not null)
            {
                regionAction();
                e.Handled = true;
                return;
            }
        }

        if (ctrl && e.Key == Windows.System.VirtualKey.V)
        {
            PasteFromClipboard();
            e.Handled = true;
            return;
        }

        if (ctrl)
        {
            System.Windows.Input.ICommand? command = e.Key switch
            {
                Windows.System.VirtualKey.Z when shift => _viewModel.RedoCommand,
                Windows.System.VirtualKey.Z => _viewModel.UndoCommand,
                Windows.System.VirtualKey.Y => _viewModel.RedoCommand,
                Windows.System.VirtualKey.S => _viewModel.SaveCommand,
                Windows.System.VirtualKey.C => _viewModel.CopyToClipboardCommand,
                _ => null,
            };
            if (command is not null)
            {
                if (command.CanExecute(null))
                {
                    command.Execute(null);
                }
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Windows.System.VirtualKey.Escape && _viewModel.SelectedAnnotation is not null)
        {
            _viewModel.SelectedAnnotation = null;
            Canvas.Invalidate();
            e.Handled = true;
            return;
        }

        if (e.Key is Windows.System.VirtualKey.Delete or Windows.System.VirtualKey.Back &&
            _viewModel.SelectedAnnotation is not null)
        {
            _viewModel.DeleteSelectedAnnotation();
            Canvas.Invalidate();
            e.Handled = true;
        }
    }

    private static bool IsKeyDown(Windows.System.VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DeleteSelectedAnnotation();
        Canvas.Invalidate();
    }

    private void BringToFrontButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedAnnotation is { } shape)
        {
            _viewModel.BringToFront(shape);
            Canvas.Invalidate();
        }
    }

    private void SendToBackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedAnnotation is { } shape)
        {
            _viewModel.SendToBack(shape);
            Canvas.Invalidate();
        }
    }

    private void MoveToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Move, MoveToolButton);
    private void SelectToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Select, SelectToolButton);
    private void RectangleToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Rectangle, RectangleToolButton);
    private void EllipseToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Ellipse, EllipseToolButton);
    private void LineToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Line, LineToolButton);
    private void ArrowToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Arrow, ArrowToolButton);
    private void PenToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Pen, PenToolButton);
    private void HighlightToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Highlight, HighlightToolButton);
    private void TextToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Text, TextToolButton);
    private void FillToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Fill, FillToolButton);
    private void MosaicToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Mosaic, MosaicToolButton);
    private void BlurToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(CaptureTool.Blur, BlurToolButton);

    private void StampItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: StampKind kind })
        {
            _selectedStampKind = kind;
            _selectedStampColor = _viewModel.StrokeColor;
            _stampSize = DefaultStampSize;
            SelectTool(CaptureTool.Stamp, StampsToolButton);
            StampsFlyout.Hide();
        }
    }

    private void NumberStampItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SolidColorBrush brush })
        {
            _selectedStampKind = StampKind.Number;
            _selectedStampColor = ToSkColor(brush.Color);
            _stampSize = DefaultStampSize;
            SelectTool(CaptureTool.Stamp, StampsToolButton);
            StampsFlyout.Hide();
        }
    }

    private void SelectTool(CaptureTool tool, ButtonBase? pressedButton)
    {
        _viewModel.SelectedTool = tool;
        _viewModel.SelectedAnnotation = null;
        SetRegion(null);
        _isCropping = false;
        CropToolButton.IsChecked = false;
        foreach (var btn in ToolButtons)
        {
            btn.IsChecked = ReferenceEquals(btn, pressedButton);
        }
        Canvas.Invalidate();
    }

    private void CropToolButton_Click(object sender, RoutedEventArgs e)
    {
        _isCropping = CropToolButton.IsChecked == true;
        if (_isCropping)
        {
            _viewModel.SelectedTool = CaptureTool.None;
            _viewModel.SelectedAnnotation = null;
            SetRegion(null);
            foreach (var btn in ToolButtons)
            {
                btn.IsChecked = false;
            }
        }
    }

    private void SizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _viewModel.StrokeWidth = (float)e.NewValue;
        ApplyStyleToSelectionIfAny();
    }

    private void Color1ColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        _viewModel.StrokeColor = ToSkColor(args.NewColor);
        Color1Swatch.Fill = new SolidColorBrush(args.NewColor);
        ApplyStyleToSelectionIfAny();
    }

    private void Color2ColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        _viewModel.FillColor = ToSkColor(args.NewColor);
        Color2Swatch.Fill = new SolidColorBrush(args.NewColor);
        ApplyStyleToSelectionIfAny();
    }

    /// <summary>Khi đang có 1 shape được chọn (Move tool), đổi Color1/Size áp dụng luôn lên shape đó
    /// thay vì chỉ ảnh hưởng shape vẽ tiếp theo - đúng hành vi "sửa lại shape đã đặt" người dùng yêu
    /// cầu. Color2 (fill) chỉ có ý nghĩa với Highlight nên không áp cho shape khác qua đường này.</summary>
    private void ApplyStyleToSelectionIfAny()
    {
        if (_viewModel.SelectedAnnotation is { } shape)
        {
            var color = shape switch
            {
                HighlightAnnotation => _viewModel.FillColor.WithAlpha(90),
                RedactAnnotation => shape.Color, // không dùng màu - chỉ Size (mức độ che) áp dụng
                _ => _viewModel.StrokeColor,
            };
            _viewModel.ChangeAnnotationStyle(shape, color, _viewModel.StrokeWidth);
            Canvas.Invalidate();
        }
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e) => _viewModel.UndoCommand.Execute(null);
    private void RedoButton_Click(object sender, RoutedEventArgs e) => _viewModel.RedoCommand.Execute(null);
    private void SaveButton_Click(object sender, RoutedEventArgs e) => _viewModel.SaveCommand.Execute(null);
    private void CopyButton_Click(object sender, RoutedEventArgs e) => _viewModel.CopyToClipboardCommand.Execute(null);
    private async void CloseButton_Click(object sender, RoutedEventArgs e) => await RequestCloseAsync();

    private enum RibbonTab { Home, File, NumberStamp, Region }

    private void RibbonTabHeader_Click(object sender, RoutedEventArgs e)
    {
        var tab = sender switch
        {
            _ when ReferenceEquals(sender, FileTabHeader) => RibbonTab.File,
            _ when ReferenceEquals(sender, NumberStampTabHeader) => RibbonTab.NumberStamp,
            _ when ReferenceEquals(sender, RegionTabHeader) => RibbonTab.Region,
            _ => RibbonTab.Home,
        };
        SelectRibbonTab(tab);
    }

    private void SelectRibbonTab(RibbonTab tab)
    {
        HomeTabHeader.IsChecked = tab == RibbonTab.Home;
        FileTabHeader.IsChecked = tab == RibbonTab.File;
        NumberStampTabHeader.IsChecked = tab == RibbonTab.NumberStamp;
        RegionTabHeader.IsChecked = tab == RibbonTab.Region;
        RegionRibbonPanel.Visibility = tab == RibbonTab.Region ? Visibility.Visible : Visibility.Collapsed;
        HomeRibbonPanel.Visibility = tab == RibbonTab.Home ? Visibility.Visible : Visibility.Collapsed;
        FileRibbonPanel.Visibility = tab == RibbonTab.File ? Visibility.Visible : Visibility.Collapsed;
        NumberStampRibbonPanel.Visibility = tab == RibbonTab.NumberStamp ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Hiện/ẩn tab contextual "Number Stamp" theo SelectedAnnotation (giống PicPick: chọn 1
    /// Number Stamp đã đặt tự nhảy sang tab này). Đồng bộ Current/Outline/Fill hiển thị theo đúng
    /// shape đang chọn - các handler ValueChanged/ColorChanged bên dưới không tạo command thừa vì
    /// EditorViewModel chỉ Do() khi giá trị mới thực sự khác giá trị cũ.</summary>
    private void UpdateNumberStampTab()
    {
        if (_viewModel.SelectedAnnotation is StampAnnotation { Kind: StampKind.Number } stamp)
        {
            NumberStampTabHeader.Visibility = Visibility.Visible;
            CurrentNumberBox.Value = stamp.NumberValue;
            StampFillSwatch.Fill = new SolidColorBrush(ToWindowsColor(stamp.Color));
            StampFillColorPicker.Color = ToWindowsColor(stamp.Color);
            StampOutlineSwatch.Fill = new SolidColorBrush(ToWindowsColor(stamp.OutlineColor));
            StampOutlineColorPicker.Color = ToWindowsColor(stamp.OutlineColor);
            SelectRibbonTab(RibbonTab.NumberStamp);
        }
        else
        {
            NumberStampTabHeader.Visibility = Visibility.Collapsed;
            if (NumberStampTabHeader.IsChecked == true)
            {
                SelectRibbonTab(RibbonTab.Home);
            }
        }
    }

    private void FlattenButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.FlattenSelectedAnnotation();
        Canvas.Invalidate();
    }

    private void StampStyleSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SolidColorBrush brush } &&
            _viewModel.SelectedAnnotation is StampAnnotation { Kind: StampKind.Number } stamp)
        {
            _viewModel.ChangeStampColors(stamp, ToSkColor(brush.Color), stamp.OutlineColor);
            StampFillSwatch.Fill = new SolidColorBrush(brush.Color);
            StampFillColorPicker.Color = brush.Color;
            Canvas.Invalidate();
        }
    }

    private void CurrentNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_viewModel.SelectedAnnotation is StampAnnotation { Kind: StampKind.Number } stamp && !double.IsNaN(args.NewValue))
        {
            _viewModel.ChangeStampNumber(stamp, (int)args.NewValue);
            Canvas.Invalidate();
        }
    }

    private void NextNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!double.IsNaN(args.NewValue))
        {
            _numberStampCounter = (int)args.NewValue;
        }
    }

    private void CurrentDecrement_Click(object sender, RoutedEventArgs e) => StepNumberBox(CurrentNumberBox, -1);
    private void CurrentIncrement_Click(object sender, RoutedEventArgs e) => StepNumberBox(CurrentNumberBox, 1);
    private void NextDecrement_Click(object sender, RoutedEventArgs e) => StepNumberBox(NextNumberBox, -1);
    private void NextIncrement_Click(object sender, RoutedEventArgs e) => StepNumberBox(NextNumberBox, 1);

    /// <summary>Gán Value mới để ValueChanged của NumberBox tự chạy logic cũ (tạo command / cập nhật bộ đếm).</summary>
    private static void StepNumberBox(NumberBox box, int delta)
    {
        double current = double.IsNaN(box.Value) ? box.Minimum : box.Value;
        box.Value = Math.Max(box.Minimum, current + delta);
    }

    private void StampOutlineColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_viewModel.SelectedAnnotation is StampAnnotation { Kind: StampKind.Number } stamp)
        {
            _viewModel.ChangeStampColors(stamp, stamp.Color, ToSkColor(args.NewColor));
            StampOutlineSwatch.Fill = new SolidColorBrush(args.NewColor);
            Canvas.Invalidate();
        }
    }

    private void StampFillColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_viewModel.SelectedAnnotation is StampAnnotation { Kind: StampKind.Number } stamp)
        {
            _viewModel.ChangeStampColors(stamp, ToSkColor(args.NewColor), stamp.OutlineColor);
            StampFillSwatch.Fill = new SolidColorBrush(args.NewColor);
            Canvas.Invalidate();
        }
    }
}
