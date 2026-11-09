using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using ScreenCapture.Models;
using ScreenCapture.Services;
using SkiaSharp;

namespace ScreenCapture.Views;

/// <summary>
/// Bảng màu (2026-11-09, kiểu Color Palette của PicPick): màu chủ đề + 5 sắc độ, màu chuẩn, 20 ô Màu tự chọn (nhớ trong
/// settings.json qua launcher); chọn màu bằng vùng Độ bão hoà × Độ sáng + thanh sắc màu, ô H / S / L (0–255), R / G / B, #;
/// Hút màu trên màn hình (tự copy mã, tự thêm vào Màu tự chọn); copy mã dạng HTML / RGB. Mở từ Editor thì có thêm Dùng làm
/// Màu nét / Màu nền. Launcher giữ 1 cửa sổ duy nhất và lưu cài đặt khi nhận <see cref="CustomColorsChanged"/> /
/// <see cref="FormatChanged"/>.
/// </summary>
public sealed partial class ColorPaletteWindow : Window
{
    private const double SwatchSize = 24;

    private readonly IClipboardService _clipboard;
    private readonly Func<Task<SKColor?>> _pickScreenColor;
    private List<string> _customColors;
    private Action<SKColor>? _applyStroke, _applyFill;
    private SKColor _color;
    /// <summary>Sắc màu đang chọn (độ) - giữ riêng vì màu xám (độ bão hoà 0) không mang sắc màu.</summary>
    private double _hue;
    private bool _syncing;
    private bool _draggingHue;

    private enum Source { None, Spectrum, Hsl, Rgb, Hex }

    /// <summary>Màu tự chọn đổi (đã thêm màu) - launcher ghi settings.json.</summary>
    public event EventHandler<IReadOnlyList<string>>? CustomColorsChanged;
    /// <summary>Đổi định dạng copy (HTML / RGB) - launcher ghi settings.json.</summary>
    public event EventHandler<ColorCopyFormat>? FormatChanged;

    /// <param name="pickScreenColor">Hút màu trên màn hình (launcher ẩn các cửa sổ, hiện overlay); null = huỷ.</param>
    public ColorPaletteWindow(IReadOnlyList<string> customColors, ColorCopyFormat format, SKColor? initial, IClipboardService clipboard,
        Func<Task<SKColor?>> pickScreenColor)
    {
        InitializeComponent();
        SharedUI.Helpers.WindowIcon.Apply(this);
        WindowChrome.Apply(this, 720, 470);
        WindowChrome.Resize(this, 830, 540);
        _clipboard = clipboard;
        _pickScreenColor = pickScreenColor;
        _customColors = [.. customColors];

        foreach (var box in new[] { HueBox, SatBox, LumBox, RedBox, GreenBox, BlueBox })
        {
            // Minimum / Maximum gán trong code (gán trong XAML từng gây XamlParseException với Slider, xem SettingsWindow).
            box.Minimum = 0;
            box.Maximum = 255;
            box.SmallChange = 1;
        }
        _syncing = true;
        FormatBox.SelectedIndex = format == ColorCopyFormat.Rgb ? 1 : 0;
        _syncing = false;

        BuildSwatches();
        SetColor(initial ?? (_customColors.Count > 0 && ColorTools.TryParseHtml(_customColors[0], out var last) ? last : new SKColor(0xED, 0x1C, 0x24)));
    }

    private ColorCopyFormat Format => FormatBox.SelectedIndex == 1 ? ColorCopyFormat.Rgb : ColorCopyFormat.Html;

    /// <summary>Mở từ Editor: hiện Dùng làm Màu nét / Màu nền; null = ẩn (mở từ cửa sổ chính / khay, hoặc Editor đã đóng).</summary>
    public void SetEditorTarget(Action<SKColor>? applyStroke, Action<SKColor>? applyFill)
    {
        _applyStroke = applyStroke;
        _applyFill = applyFill;
        EditorTargetPanel.Visibility = applyStroke is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Màu vừa hút bằng phím tắt khi Bảng màu đang mở: chọn màu đó (Màu tự chọn đã được launcher cập nhật).</summary>
    public void ShowPickedColor(SKColor color, IReadOnlyList<string> customColors)
    {
        _customColors = [.. customColors];
        BuildCustomSwatches();
        SetColor(color);
    }

    // ---------- Ô màu ----------

    private void BuildSwatches()
    {
        var baseRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var shadeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var color in ColorTools.ThemeColors)
        {
            baseRow.Children.Add(Swatch(color));
            var column = new StackPanel();
            foreach (var shade in ColorTools.Shades(color))
            {
                column.Children.Add(Swatch(shade, height: 20));
            }
            shadeRow.Children.Add(column);
        }
        ThemePanel.Children.Add(baseRow);
        ThemePanel.Children.Add(shadeRow);
        foreach (var color in ColorTools.StandardColors)
        {
            StandardPanel.Children.Add(Swatch(color));
        }
        BuildCustomSwatches();
    }

    private void BuildCustomSwatches()
    {
        CustomPanel.Children.Clear();
        for (int row = 0; row < 2; row++)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            for (int col = 0; col < 10; col++)
            {
                int index = row * 10 + col;
                line.Children.Add(index < _customColors.Count && ColorTools.TryParseHtml(_customColors[index], out var color)
                    ? Swatch(color)
                    : new Border
                    {
                        Width = SwatchSize,
                        Height = SwatchSize,
                        BorderThickness = new Thickness(1),
                        BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                        Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                    });
            }
            CustomPanel.Children.Add(line);
        }
    }

    private Button Swatch(SKColor color, double height = SwatchSize)
    {
        // Màu nằm trong Rectangle (không đặt Background của nút): rê chuột vào thì nút đổi nền, màu vẫn giữ.
        var button = new Button
        {
            Width = SwatchSize,
            Height = height,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            BorderThickness = new Thickness(0),
            Content = new Rectangle
            {
                Width = SwatchSize,
                Height = height,
                Fill = new SolidColorBrush(ToWindows(color)),
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 128, 128, 128)),
                StrokeThickness = 1,
            },
        };
        string hex = ColorTools.ToHtml(color);
        ToolTipService.SetToolTip(button, $"{hex}  ·  {ColorTools.ToRgb(color)}");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, hex);
        button.Click += (_, _) => SetColor(color);
        return button;
    }

    // ---------- Màu đang chọn ----------

    private void SetColor(SKColor color, Source source = Source.None, double? hue = null)
    {
        _color = color.WithAlpha(255);
        _color.ToHsv(out float h, out float s, out float v);
        if (hue is { } explicitHue)
        {
            _hue = explicitHue;
        }
        else if (s > 0.5f)
        {
            _hue = h;
        }

        _syncing = true;
        try
        {
            if (source != Source.Spectrum)
            {
                Spectrum.HsvColor = new Vector4((float)_hue, s / 100f, v / 100f, 1f);
            }
            if (source != Source.Hsl)
            {
                var (hh, ss, ll) = ColorTools.ToHsl(_color);
                HueBox.Value = s > 0.5f ? hh : Math.Round(_hue / 360 * 255) % 256;
                SatBox.Value = ss;
                LumBox.Value = ll;
            }
            if (source != Source.Rgb)
            {
                RedBox.Value = _color.Red;
                GreenBox.Value = _color.Green;
                BlueBox.Value = _color.Blue;
            }
            if (source != Source.Hex)
            {
                HexBox.Text = ColorTools.ToHtml(_color).TrimStart('#');
            }
            PreviewSwatch.Fill = new SolidColorBrush(ToWindows(_color));
            CodeBox.Text = ColorTools.Format(_color, Format);
            UpdateHueMarker();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void Spectrum_ColorChanged(ColorSpectrum sender, ColorChangedEventArgs args)
    {
        if (_syncing)
        {
            return;
        }
        var c = args.NewColor;
        SetColor(new SKColor(c.R, c.G, c.B), Source.Spectrum, _hue);
    }

    private void HslBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncing || double.IsNaN(HueBox.Value) || double.IsNaN(SatBox.Value) || double.IsNaN(LumBox.Value))
        {
            return;
        }
        int h = (int)HueBox.Value, s = (int)SatBox.Value, l = (int)LumBox.Value;
        SetColor(ColorTools.FromHsl(h, s, l), Source.Hsl, h / 255.0 * 360);
    }

    private void RgbBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncing || double.IsNaN(RedBox.Value) || double.IsNaN(GreenBox.Value) || double.IsNaN(BlueBox.Value))
        {
            return;
        }
        SetColor(new SKColor((byte)RedBox.Value, (byte)GreenBox.Value, (byte)BlueBox.Value), Source.Rgb);
    }

    private void HexBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            ApplyHex();
            e.Handled = true;
        }
    }

    private void HexBox_LostFocus(object sender, RoutedEventArgs e) => ApplyHex();

    private void ApplyHex()
    {
        if (ColorTools.TryParseHtml(HexBox.Text, out var color))
        {
            SetColor(color, Source.Hex);
        }
        else
        {
            HexBox.Text = ColorTools.ToHtml(_color).TrimStart('#'); // gõ sai → trả lại mã của màu đang chọn
        }
    }

    // ---------- Thanh sắc màu ----------

    private void HueBar_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _draggingHue = HueBar.CapturePointer(e.Pointer);
        SetHueFromPointer(e);
    }

    private void HueBar_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_draggingHue)
        {
            SetHueFromPointer(e);
        }
    }

    private void HueBar_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _draggingHue = false;
        HueBar.ReleasePointerCapture(e.Pointer);
    }

    private void HueBar_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateHueMarker();

    private void SpectrumHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double side = Math.Max(120, Math.Floor(Math.Min(e.NewSize.Width, e.NewSize.Height)));
        Spectrum.Width = Spectrum.Height = side;
        HueColumn.Height = side;
    }

    private void SetHueFromPointer(PointerRoutedEventArgs e)
    {
        double y = e.GetCurrentPoint(HueBar).Position.Y;
        double hue = Math.Clamp(y / Math.Max(1, HueBar.ActualHeight), 0, 1) * 360;
        var hsv = Spectrum.HsvColor;
        var color = SkHsv(hue, hsv.Y, hsv.Z);
        SetColor(color, Source.None, hue);
    }

    private static SKColor SkHsv(double hue, float s, float v) => SKColor.FromHsv((float)(hue % 360), s * 100, v * 100);

    /// <summary>Tam giác chỉ sắc màu (cao 10 px: đỉnh giữa ở y + 5).</summary>
    private void UpdateHueMarker() => Canvas.SetTop(HueMarker, _hue / 360 * Math.Max(0, HueBar.ActualHeight) - 5);

    // ---------- Nút ----------

    private void AddCustomButton_Click(object sender, RoutedEventArgs e) => AddCustomColor(_color);

    private void AddCustomColor(SKColor color)
    {
        _customColors = ColorTools.AddCustomColor(_customColors, color);
        BuildCustomSwatches();
        CustomColorsChanged?.Invoke(this, _customColors);
    }

    private async void PickButton_Click(object sender, RoutedEventArgs e)
    {
        if (await _pickScreenColor() is { } color)
        {
            SetColor(color);
            AddCustomColor(color);
            Copy();
        }
    }

    private void UseAsStrokeButton_Click(object sender, RoutedEventArgs e) => _applyStroke?.Invoke(_color);

    private void UseAsFillButton_Click(object sender, RoutedEventArgs e) => _applyFill?.Invoke(_color);

    private void FormatBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }
        CodeBox.Text = ColorTools.Format(_color, Format);
        FormatChanged?.Invoke(this, Format);
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e) => Copy();

    /// <summary>Copy mã màu theo định dạng đang chọn; chữ trên nút đổi thành "Đã copy" 1,5 giây.</summary>
    private void Copy()
    {
        _clipboard.CopyText(CodeBox.Text);
        CopyButtonText.Text = "Đã copy";
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1.5);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => CopyButtonText.Text = "Copy";
        timer.Start();
    }

    private static Windows.UI.Color ToWindows(SKColor c) => Windows.UI.Color.FromArgb(255, c.Red, c.Green, c.Blue);
}
