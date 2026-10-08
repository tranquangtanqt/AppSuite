using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;

namespace ScreenCapture.Views;

/// <summary>1 mẫu kích thước trong hộp thoại Ảnh mới.</summary>
public sealed record NewImagePreset(string Label, int Width, int Height)
{
    public override string ToString() => Label;
}

/// <summary>Hộp thoại "Ảnh mới" - xem NewImageDialog.xaml. Bên gọi đưa danh sách mẫu (mẫu đầu = mặc định) và màu nền
/// lần trước; bấm Tạo → <see cref="CreateBitmap"/>.</summary>
public sealed partial class NewImageDialog : ContentDialog
{
    /// <summary>Cạnh tối đa (1 chiều dài như ảnh chụp cuộn trang vẫn được).</summary>
    public const int MaxSide = 16384;

    /// <summary>Tổng số pixel tối đa: 64 triệu BGRA ≈ 256 MB / ảnh (vd 8000 × 8000; ảnh 4K = 8,3 triệu). 16384 × 16384 ≈ 1 GB
    /// mỗi bản, cộng bản giữ cho Undo → dễ hết bộ nhớ khi vẽ / lưu.</summary>
    public const long MaxPixels = 64_000_000;

    /// <summary>Lý do cỡ ảnh không hợp lệ (để hiện trong hộp thoại), null = hợp lệ. Dùng chung với Đổi cỡ ảnh.</summary>
    public static string? SizeError(double width, double height)
    {
        if (double.IsNaN(width) || double.IsNaN(height) || width is < 1 or > MaxSide || height is < 1 or > MaxSide)
        {
            return $"Nhập rộng / cao từ 1 đến {MaxSide} px.";
        }
        double pixels = Math.Round(width) * Math.Round(height);
        return pixels > MaxPixels
            ? $"Ảnh {Math.Round(width)} × {Math.Round(height)} px = {pixels / 1e6:0.#} triệu pixel - quá lớn (tối đa {MaxPixels / 1_000_000} triệu, vd 8000 × 8000)."
            : null;
    }

    private const string CustomLabel = "Tuỳ chỉnh";

    private readonly List<NewImagePreset> _presets;
    private bool _syncing;

    public NewImageDialog(IReadOnlyList<NewImagePreset> presets, SKColor backColor)
    {
        InitializeComponent();
        _presets = [.. presets];

        // Minimum/Maximum của NumberBox gán qua code - gán qua XAML attribute từng gây XamlParseException (xem EditorWindow).
        foreach (var box in new[] { WidthBox, HeightBox })
        {
            box.Minimum = 1;
            box.Maximum = MaxSide;
            box.SmallChange = 10;
            box.LargeChange = 100;
        }

        foreach (var preset in _presets)
        {
            PresetBox.Items.Add(preset);
        }
        PresetBox.Items.Add(CustomLabel);
        PresetBox.SelectedIndex = 0;

        SetColor(backColor, syncPicker: true);
    }

    public int ImageWidth => (int)Math.Round(WidthBox.Value);
    public int ImageHeight => (int)Math.Round(HeightBox.Value);
    public SKColor BackColor { get; private set; }

    /// <summary>Ảnh trống đúng cỡ, tô màu nền (cùng định dạng pixel với ảnh chụp - xem CaptureService).</summary>
    public SKBitmap CreateBitmap()
    {
        var bitmap = new SKBitmap(new SKImageInfo(ImageWidth, ImageHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.Erase(BackColor);
        return bitmap;
    }

    private void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || PresetBox.SelectedItem is not NewImagePreset preset)
        {
            return; // "Tuỳ chỉnh": giữ nguyên số đang nhập
        }
        _syncing = true;
        WidthBox.Value = preset.Width;
        HeightBox.Value = preset.Height;
        _syncing = false;
        UpdateSummary();
    }

    /// <summary>Nhập tay: cỡ khớp 1 mẫu thì chọn mẫu đó, không thì chuyển sang "Tuỳ chỉnh".</summary>
    private void SizeBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncing)
        {
            return;
        }
        if (!double.IsNaN(sender.Value) && sender.Value != Math.Round(sender.Value))
        {
            sender.Value = Math.Round(sender.Value); // px nguyên - gọi lại handler này
            return;
        }
        if (!double.IsNaN(WidthBox.Value) && !double.IsNaN(HeightBox.Value)) // cả cỡ quá lớn cũng đổi ô mẫu sang Tuỳ chỉnh
        {
            _syncing = true;
            var match = PresetBox.SelectedItem is NewImagePreset selected && selected.Width == ImageWidth && selected.Height == ImageHeight
                ? selected
                : _presets.FirstOrDefault(p => p.Width == ImageWidth && p.Height == ImageHeight);
            PresetBox.SelectedItem = match is null ? CustomLabel : match;
            _syncing = false;
        }
        UpdateSummary();
    }

    private void SwapButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (!IsValid)
        {
            return;
        }
        (WidthBox.Value, HeightBox.Value) = (HeightBox.Value, WidthBox.Value);
    }

    private void ColorBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && ColorBox.SelectedItem is ComboBoxItem { Tag: string tag } && tag != "custom")
        {
            SetColor(tag == TransparentTag ? SKColors.Transparent : SKColor.Parse(tag), syncPicker: true);
        }
    }

    private void BackColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (!_syncing)
        {
            var c = args.NewColor;
            SetColor(new SKColor(c.R, c.G, c.B), syncPicker: false);
        }
    }

    private const string TransparentTag = "transparent";

    /// <summary>Alpha = 0 → nền trong suốt; màu khác luôn đục hẳn.</summary>
    private void SetColor(SKColor color, bool syncPicker)
    {
        bool transparent = color.Alpha == 0;
        BackColor = transparent ? SKColors.Transparent : color.WithAlpha(255);
        _syncing = true;
        if (syncPicker && !transparent)
        {
            BackColorPicker.Color = Windows.UI.Color.FromArgb(255, color.Red, color.Green, color.Blue);
        }
        string tag = transparent ? TransparentTag : $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
        ColorBox.SelectedItem = ColorBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag)
            ?? ColorBox.Items.OfType<ComboBoxItem>().Last();
        _syncing = false;
        Swatch.Background = transparent
            ? CheckerBrush()
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, color.Red, color.Green, color.Blue));
    }

    /// <summary>Sọc chéo trắng - xám nhạt: ô xem trước cho nền "Trong suốt".</summary>
    private static Brush CheckerBrush()
    {
        var gradient = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(8, 8), MappingMode = BrushMappingMode.Absolute, SpreadMethod = GradientSpreadMethod.Repeat };
        gradient.GradientStops.Add(new GradientStop { Color = Microsoft.UI.Colors.White, Offset = 0 });
        gradient.GradientStops.Add(new GradientStop { Color = Microsoft.UI.Colors.White, Offset = 0.5 });
        gradient.GradientStops.Add(new GradientStop { Color = Microsoft.UI.Colors.LightGray, Offset = 0.5 });
        gradient.GradientStops.Add(new GradientStop { Color = Microsoft.UI.Colors.LightGray, Offset = 1 });
        return gradient;
    }

    private bool IsValid => SizeError(WidthBox.Value, HeightBox.Value) is null;

    private void UpdateSummary()
    {
        var error = SizeError(WidthBox.Value, HeightBox.Value);
        IsPrimaryButtonEnabled = error is null;
        SummaryText.Text = error ?? $"Tạo ảnh trống {ImageWidth} × {ImageHeight} px thành 1 tab mới trong Editor.";
    }
}
