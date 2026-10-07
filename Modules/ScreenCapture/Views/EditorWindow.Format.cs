using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using ScreenCapture.Models;
using SkiaSharp;

namespace ScreenCapture.Views;

/// <summary>Tab contextual "Định dạng" (phông / cỡ / đậm / nghiêng, nền + viền chữ, tô nền hình, bo góc, kiểu nét, độ đục,
/// đầu mũi tên) và việc áp màu / cỡ nét / định dạng lên shape đang chọn.</summary>
public sealed partial class EditorWindow
{
    /// <summary>Định dạng cho shape vẽ tiếp theo của từng công cụ (giữ suốt phiên chạy, mỗi công cụ 1 bộ riêng - đường
    /// thẳng không lấy đầu mũi tên của công cụ Mũi tên). Color1 / Color2 / Size là thiết lập chung (EditorViewModel), lấy
    /// lúc vẽ: FillColor / BackgroundColor ở đây chỉ đánh dấu "có tô" (khác null), màu thật là Color2.</summary>
    private readonly Dictionary<CaptureTool, AnnotationShape> _toolDefaults = new()
    {
        [CaptureTool.Rectangle] = new RectangleAnnotation(),
        [CaptureTool.Ellipse] = new EllipseAnnotation(),
        [CaptureTool.Line] = new LineArrowAnnotation { EndHead = ArrowHead.None },
        [CaptureTool.Arrow] = new LineArrowAnnotation(),
        [CaptureTool.Pen] = new FreehandAnnotation(),
        [CaptureTool.Text] = new TextAnnotation(),
        [CaptureTool.Callout] = new CalloutAnnotation { FillColor = SKColors.White },
    };

    /// <summary>Màu viền chữ chọn gần nhất - giữ lại khi tắt rồi bật lại ô "Viền chữ".</summary>
    private SKColor _textOutlineColor = SKColors.White;

    /// <summary>Đang đổ giá trị của shape lên các control → bỏ qua sự kiện ValueChanged / SelectionChanged của chúng.</summary>
    private bool _syncingFormat;

    private IReadOnlyList<string> _fontFamilies = [];

    /// <summary>Cỡ chữ có sẵn trong ô Cỡ chữ (như Word); gõ số khác trong khoảng MinFontSize..MaxFontSize vẫn được.</summary>
    private static readonly double[] FontSizePresets = [8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 40, 48, 56, 64, 72, 96, 128];
    private const double MinFontSize = 6;
    private const double MaxFontSize = 400;

    private static string FormatFontSize(double size) => size.ToString("0.#", CultureInfo.InvariantCulture);

    private readonly ObservableCollection<string> _fontSizeItems = new(FontSizePresets.Select(FormatFontSize));
    /// <summary>Cỡ không có sẵn đang được chèn tạm vào _fontSizeItems (xem ShowFontSize).</summary>
    private string? _customFontSize;

    private static bool IsFormattable(AnnotationShape shape) =>
        shape is RectangleAnnotation or EllipseAnnotation or LineArrowAnnotation or FreehandAnnotation or TextAnnotation or CalloutAnnotation;

    /// <summary>Shape mà tab Định dạng đang hiển thị / sửa: chữ đang gõ trên ảnh, shape đang chọn, không có thì bộ định dạng
    /// của công cụ đang dùng.</summary>
    private AnnotationShape? FormatTarget =>
        _inlineTarget ?? (_viewModel.SelectedAnnotation is { } selected && IsFormattable(selected)
            ? selected
            : _toolDefaults.GetValueOrDefault(_viewModel.SelectedTool));

    private void InitFormatControls()
    {
        _syncingFormat = true; // đặt Minimum làm Slider tự đổi Value → không được coi là người dùng đổi định dạng
        try
        {
            _fontFamilies = FontCache.InstalledFamilies();
            FontFamilyBox.ItemsSource = _fontFamilies;
            // Minimum / Maximum gán qua code (gán trong XAML từng gây XamlParseException - xem SizeSlider).
            FontSizeBox.ItemsSource = _fontSizeItems;
            CornerRadiusSlider.Minimum = 0;
            CornerRadiusSlider.Maximum = 40;
            OpacitySlider.Minimum = 10;
            OpacitySlider.Maximum = 100;
            OpacitySlider.StepFrequency = 5;
            OpacitySlider.Value = 100;
            TextOutlineColorPicker.Color = ToWindowsColor(_textOutlineColor);
        }
        finally
        {
            _syncingFormat = false;
        }
    }

    /// <summary>Shape mới vẽ bằng công cụ đang dùng: định dạng của công cụ + Color1 / Size / Color2 hiện tại.</summary>
    private T Styled<T>(T shape) where T : AnnotationShape
    {
        if (_toolDefaults.TryGetValue(_viewModel.SelectedTool, out var defaults) && defaults.GetType() == shape.GetType())
        {
            shape.CopyPropertiesFrom(defaults);
        }
        shape.Color = _viewModel.StrokeColor;
        shape.StrokeWidth = _viewModel.StrokeWidth;
        if (shape.FillColor is not null)
        {
            shape.FillColor = _viewModel.FillColor;
        }
        if (shape is TextAnnotation { BackgroundColor: not null } text)
        {
            text.BackgroundColor = _viewModel.FillColor;
        }
        return shape;
    }

    /// <summary>Hiện / ẩn tab Định dạng và các nhóm hợp với shape, đổ giá trị của shape lên control.
    /// <paramref name="switchToTab"/>: mở luôn tab này (vừa chọn 1 shape định dạng được - giống tab Number Stamp).</summary>
    private void UpdateFormatTab(bool switchToTab = false)
    {
        var target = FormatTarget;
        if (target is null)
        {
            FormatTabHeader.Visibility = Visibility.Collapsed;
            if (FormatTabHeader.IsChecked == true)
            {
                SelectRibbonTab(RibbonTab.Home);
            }
            return;
        }

        FormatTabHeader.Visibility = Visibility.Visible;
        FontGroup.Visibility = VisibleIf(target is ITextShape);
        TextDecorGroup.Visibility = VisibleIf(target is TextAnnotation);
        ShapeFillGroup.Visibility = VisibleIf(target is RectangleAnnotation or EllipseAnnotation or CalloutAnnotation);
        CornerRadiusRow.Visibility = VisibleIf(target is RectangleAnnotation or CalloutAnnotation);
        DashBox.Visibility = VisibleIf(target is not TextAnnotation);
        ArrowHeadGroup.Visibility = VisibleIf(target is LineArrowAnnotation);

        _syncingFormat = true;
        try
        {
            if (target is ITextShape text)
            {
                FontFamilyBox.SelectedItem = _fontFamilies.FirstOrDefault(f => string.Equals(f, text.FontFamily, StringComparison.OrdinalIgnoreCase));
                ShowFontSize(text.FontSize);
                BoldButton.IsChecked = text.Bold;
                ItalicButton.IsChecked = text.Italic;
            }
            if (target is TextAnnotation textShape)
            {
                TextBackgroundCheck.IsChecked = textShape.BackgroundColor is not null;
                TextOutlineCheck.IsChecked = textShape.OutlineColor is not null;
                if (textShape.OutlineColor is { } outline)
                {
                    _textOutlineColor = outline;
                }
                TextOutlineSwatch.Fill = new SolidColorBrush(ToWindowsColor(_textOutlineColor));
                TextOutlineColorPicker.Color = ToWindowsColor(_textOutlineColor);
            }
            ShapeFillCheck.IsChecked = target.FillColor is not null;
            CornerRadiusSlider.Value = target switch
            {
                RectangleAnnotation rectangle => rectangle.CornerRadius,
                CalloutAnnotation callout => callout.CornerRadius,
                _ => 0,
            };
            DashBox.SelectedIndex = (int)target.Dash;
            OpacitySlider.Value = Math.Round(target.Opacity * 100);
            if (target is LineArrowAnnotation line)
            {
                StartHeadBox.SelectedIndex = (int)line.StartHead;
                EndHeadBox.SelectedIndex = (int)line.EndHead;
            }
        }
        finally
        {
            _syncingFormat = false;
        }

        if (switchToTab)
        {
            SelectRibbonTab(RibbonTab.Format);
        }
    }

    private static Visibility VisibleIf(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Áp 1 thay đổi định dạng lên bộ định dạng của công cụ đang dùng (shape vẽ tiếp theo) và lên shape đang chọn
    /// (1 bước Undo). <paramref name="change"/> trả false nếu không áp được cho loại shape đó / giá trị không đổi.</summary>
    private void ApplyFormat(string description, Func<AnnotationShape, bool> change)
    {
        if (_syncingFormat)
        {
            return;
        }
        if (_toolDefaults.TryGetValue(_viewModel.SelectedTool, out var defaults))
        {
            change(defaults);
        }
        ApplyToSelection(description, change);
        UpdateFormatTab();
    }

    /// <summary>Đổi shape đang chọn thành 1 bước Undo. Chữ / khung chú thích tự đổi cỡ khung theo chữ.</summary>
    private void ApplyToSelection(string description, Func<AnnotationShape, bool> change)
    {
        if (_viewModel.SelectedAnnotation is not { } shape)
        {
            return;
        }
        var before = shape.Snapshot();
        if (!change(shape))
        {
            return;
        }
        switch (shape)
        {
            case TextAnnotation text:
                text.FitBounds();
                break;
            case CalloutAnnotation callout:
                callout.GrowToFitText();
                break;
        }
        var after = shape.Snapshot();
        shape.RestoreFrom(before);
        _viewModel.ChangeShape(shape, before, after, description);
        Canvas.Invalidate();
    }

    // ---- Màu & Cỡ nét (nhóm dùng chung tab Trang chủ / Định dạng) ----

    private void SizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        float width = (float)e.NewValue;
        _viewModel.StrokeWidth = width;
        // Mọi loại shape (Mosaic / Làm mờ: Size là mức độ che).
        ApplyToSelection("Đổi cỡ nét", s => Set(s.StrokeWidth, width, v => s.StrokeWidth = v));
    }

    private void Color1ColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        var color = ToSkColor(args.NewColor);
        _viewModel.StrokeColor = color;
        Color1Swatch.Fill = new SolidColorBrush(args.NewColor);
        // Highlight dùng Color2, Mosaic / Làm mờ không có màu.
        ApplyToSelection("Đổi màu", s => s is not (HighlightAnnotation or RedactAnnotation) && Set(s.Color, color, v => s.Color = v));
    }

    private void Color2ColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        var color = ToSkColor(args.NewColor);
        _viewModel.FillColor = color;
        Color2Swatch.Fill = new SolidColorBrush(args.NewColor);
        // Color2 = màu Highlight, nền hình / khung chú thích / ô chữ (chỉ khi shape đó đang bật tô nền).
        ApplyToSelection("Đổi màu nền", s => s switch
        {
            HighlightAnnotation => Set(s.Color, color.WithAlpha(90), v => s.Color = v),
            TextAnnotation { BackgroundColor: not null } text => Set(text.BackgroundColor, color, v => text.BackgroundColor = v),
            RectangleAnnotation or EllipseAnnotation or CalloutAnnotation when s.FillColor is not null => Set(s.FillColor, color, v => s.FillColor = v),
            _ => false,
        });
    }

    /// <summary>Gán <paramref name="value"/> nếu khác giá trị hiện tại; trả true nếu đã đổi.</summary>
    private static bool Set<T>(T current, T value, Action<T> assign)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return false;
        }
        assign(value);
        return true;
    }

    // ---- Tab Định dạng ----

    private void FontFamilyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FontFamilyBox.SelectedItem is string family)
        {
            ApplyFormat("Đổi phông chữ", s => s is ITextShape t && Set(t.FontFamily, family, v => t.FontFamily = v));
        }
    }

    /// <summary>Hiện cỡ chữ lên ô Cỡ chữ. Cỡ không có sẵn (gõ 50, Ctrl + kéo ra 40.8) được chèn tạm vào danh sách (bỏ khi
    /// sang cỡ khác): ComboBox gõ được luôn hiện lại chữ của SelectedItem sau khi gõ - SelectedItem null thì ô trắng.</summary>
    private void ShowFontSize(double size)
    {
        string text = FormatFontSize(Math.Round(size, 1));
        if (_customFontSize is { } old && old != text)
        {
            _fontSizeItems.Remove(old);
            _customFontSize = null;
        }
        if (!_fontSizeItems.Contains(text))
        {
            double value = double.Parse(text, CultureInfo.InvariantCulture);
            int index = _fontSizeItems.TakeWhile(s => double.Parse(s, CultureInfo.InvariantCulture) < value).Count();
            _fontSizeItems.Insert(index, text);
            _customFontSize = text;
        }
        FontSizeBox.SelectedItem = text;
    }

    private void FontSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FontSizeBox.SelectedItem is string text)
        {
            ApplyFontSize(text);
        }
    }

    /// <summary>Gõ số vào ô Cỡ chữ rồi Enter / rời ô. Tự xử lý (Handled) để ô không đổi SelectedItem thành chữ lạ; áp cỡ
    /// SAU khi ComboBox xong lượt gõ (nó còn đặt lại chữ trong ô sau sự kiện này).</summary>
    private void FontSizeBox_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        args.Handled = true;
        string submitted = args.Text;
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyFontSize(submitted);
            // Áp được thì ApplyFormat đã hiện lại cỡ mới; gõ sai (chữ, số âm...) / cỡ không đổi → hiện lại cỡ hiện tại.
            if (FormatTarget is ITextShape text)
            {
                _syncingFormat = true;
                try
                {
                    ShowFontSize(text.FontSize);
                }
                finally
                {
                    _syncingFormat = false;
                }
            }
        });
    }

    private bool ApplyFontSize(string text)
    {
        if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || double.IsNaN(value) || value <= 0)
        {
            return false;
        }
        float size = (float)Math.Clamp(value, MinFontSize, MaxFontSize);
        ApplyFormat("Đổi cỡ chữ", s => s is ITextShape t && Set(t.FontSize, size, v => t.FontSize = v));
        return true;
    }

    private void BoldButton_Click(object sender, RoutedEventArgs e)
    {
        bool bold = BoldButton.IsChecked == true;
        ApplyFormat(bold ? "Chữ đậm" : "Bỏ chữ đậm", s => s is ITextShape t && Set(t.Bold, bold, v => t.Bold = v));
    }

    private void ItalicButton_Click(object sender, RoutedEventArgs e)
    {
        bool italic = ItalicButton.IsChecked == true;
        ApplyFormat(italic ? "Chữ nghiêng" : "Bỏ chữ nghiêng", s => s is ITextShape t && Set(t.Italic, italic, v => t.Italic = v));
    }

    private void TextBackgroundCheck_Click(object sender, RoutedEventArgs e)
    {
        SKColor? background = TextBackgroundCheck.IsChecked == true ? _viewModel.FillColor : null;
        ApplyFormat(background is null ? "Bỏ nền ô chữ" : "Nền ô chữ",
            s => s is TextAnnotation t && Set(t.BackgroundColor, background, v => t.BackgroundColor = v));
    }

    private void TextOutlineCheck_Click(object sender, RoutedEventArgs e)
    {
        SKColor? outline = TextOutlineCheck.IsChecked == true ? _textOutlineColor : null;
        ApplyFormat(outline is null ? "Bỏ viền chữ" : "Viền chữ",
            s => s is TextAnnotation t && Set(t.OutlineColor, outline, v => t.OutlineColor = v));
    }

    /// <summary>Chọn màu viền = bật luôn viền chữ (chọn màu mà không thấy viền thì khó hiểu).</summary>
    private void TextOutlineColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_syncingFormat)
        {
            return;
        }
        _textOutlineColor = ToSkColor(args.NewColor);
        TextOutlineSwatch.Fill = new SolidColorBrush(args.NewColor);
        SKColor? outline = _textOutlineColor;
        ApplyFormat("Đổi màu viền chữ", s => s is TextAnnotation t && Set(t.OutlineColor, outline, v => t.OutlineColor = v));
    }

    private void ShapeFillCheck_Click(object sender, RoutedEventArgs e)
    {
        SKColor? fill = ShapeFillCheck.IsChecked == true ? _viewModel.FillColor : null;
        ApplyFormat(fill is null ? "Bỏ tô nền" : "Tô nền",
            s => s is RectangleAnnotation or EllipseAnnotation or CalloutAnnotation && Set(s.FillColor, fill, v => s.FillColor = v));
    }

    private void CornerRadiusSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        float radius = (float)e.NewValue;
        ApplyFormat("Đổi bo góc", s => s switch
        {
            RectangleAnnotation rectangle => Set(rectangle.CornerRadius, radius, v => rectangle.CornerRadius = v),
            CalloutAnnotation callout => Set(callout.CornerRadius, radius, v => callout.CornerRadius = v),
            _ => false,
        });
    }

    private void DashBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DashBox.SelectedIndex >= 0)
        {
            var dash = (LineDash)DashBox.SelectedIndex;
            ApplyFormat("Đổi kiểu nét", s => s is not TextAnnotation && Set(s.Dash, dash, v => s.Dash = v));
        }
    }

    private void OpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        float opacity = (float)Math.Clamp(e.NewValue / 100, 0.1, 1);
        ApplyFormat("Đổi độ trong suốt", s => Set(s.Opacity, opacity, v => s.Opacity = v));
    }

    private void ArrowHeadBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedIndex: >= 0 } box)
        {
            return;
        }
        var head = (ArrowHead)box.SelectedIndex;
        bool start = ReferenceEquals(box, StartHeadBox);
        ApplyFormat("Đổi đầu mũi tên", s => s is LineArrowAnnotation line && (start
            ? Set(line.StartHead, head, v => line.StartHead = v)
            : Set(line.EndHead, head, v => line.EndHead = v)));
    }
}
