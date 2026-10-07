using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScreenCapture.Models;
using SkiaSharp;
using SkiaSharp.Views.Windows;

namespace ScreenCapture.Views;

/// <summary>Hộp thoại chung cho các hiệu ứng có tuỳ chọn (viền, đổ bóng, mép rách, độ sáng / tương phản, watermark):
/// cột trái là các tuỳ chọn (bên gọi thêm bằng Add...), cột phải xem trước kết quả trên ảnh thu nhỏ - kể cả các hình đã vẽ
/// (hiệu ứng chỉ áp lên ảnh nền, hình vẽ nằm trên). Đổi tuỳ chọn nào cũng vẽ lại xem trước ngay.</summary>
public sealed class ImageEffectDialog : ContentDialog
{
    private const double PreviewWidth = 380;
    private const double PreviewHeight = 285;

    private readonly SKBitmap _baseImage;
    private readonly IReadOnlyList<AnnotationShape> _shapes;
    private readonly SKBitmap _previewSource;
    private readonly float _scale;
    private readonly StackPanel _columns = new() { Orientation = Orientation.Horizontal, Spacing = 16 };
    private StackPanel _controls = NewColumn();
    /// <summary>Ảnh xem trước. Dùng Image + WriteableBitmap, không dùng SKXamlCanvas: SKXamlCanvas trong ContentDialog không
    /// vẽ gì (thử trong Sandbox 2026-10-06).</summary>
    private readonly Image _preview = new() { Width = PreviewWidth, Height = PreviewHeight, Stretch = Stretch.Uniform };

    /// <summary>Hiệu ứng cần xem trước: (ảnh nền, tỉ lệ so với ảnh thật) → kết quả.</summary>
    public Func<SKBitmap, float, EffectResult>? Effect { get; set; }

    public ImageEffectDialog(string title, SKBitmap baseImage, IReadOnlyList<AnnotationShape> shapes)
    {
        Title = title;
        PrimaryButtonText = "Áp dụng";
        CloseButtonText = "Huỷ";
        DefaultButton = ContentDialogButton.Primary;
        Resources["ContentDialogMaxWidth"] = 960.0;
        _baseImage = baseImage;
        _shapes = shapes;

        // Ảnh thu nhỏ vừa ô xem trước (×2 cho màn hình DPI cao) - hiệu ứng chạy trên ảnh nhỏ nên kéo thanh trượt vẫn mượt.
        _scale = (float)Math.Min(1, Math.Min(PreviewWidth * 2 / baseImage.Width, PreviewHeight * 2 / baseImage.Height));
        var info = new SKImageInfo(Math.Max(1, (int)(baseImage.Width * _scale)), Math.Max(1, (int)(baseImage.Height * _scale)),
            SKColorType.Bgra8888, SKAlphaType.Premul);
        _previewSource = _scale < 1 ? baseImage.Resize(info, SKFilterQuality.High) ?? baseImage.Copy() : baseImage.Copy();

        var previewFrame = new Border
        {
            Child = _preview,
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            VerticalAlignment = VerticalAlignment.Top,
        };
        var layout = new Grid { ColumnSpacing = 16 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _columns.Children.Add(_controls);
        layout.Children.Add(_columns);
        Grid.SetColumn(previewFrame, 1);
        layout.Children.Add(previewFrame);
        Content = layout;
        Closed += (_, _) => _previewSource.Dispose();
    }

    // ---- Tuỳ chọn ----

    private static StackPanel NewColumn() => new() { Spacing = 10, Width = 230 };

    /// <summary>Các tuỳ chọn thêm sau đây nằm ở cột mới bên phải (hộp thoại nhiều tuỳ chọn khỏi phải cuộn).</summary>
    public void AddColumnBreak()
    {
        _controls = NewColumn();
        _columns.Children.Add(_controls);
    }

    /// <param name="format">Cách hiện giá trị cạnh nhãn, vd "{0:0} px".</param>
    public Slider AddSlider(string label, double min, double max, double value, double step, string format, Action<double> onChange)
    {
        var slider = new Slider { StepFrequency = step, SmallChange = step };
        // Minimum/Maximum gán qua code (gán qua XAML từng gây XamlParseException - xem EditorWindow).
        slider.Minimum = min;
        slider.Maximum = max;
        slider.Value = value;
        slider.Header = $"{label}: {string.Format(format, value)}";
        slider.ValueChanged += (_, e) =>
        {
            slider.Header = $"{label}: {string.Format(format, e.NewValue)}";
            onChange(e.NewValue);
            Refresh();
        };
        _controls.Children.Add(slider);
        return slider;
    }

    public Button AddColor(string label, SKColor color, Action<SKColor> onChange)
    {
        var swatch = new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Width = 22,
            Height = 22,
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, color.Red, color.Green, color.Blue)),
            Stroke = new SolidColorBrush(Microsoft.UI.Colors.Gray),
            StrokeThickness = 1,
        };
        var picker = new ColorPicker
        {
            IsAlphaEnabled = false,
            IsMoreButtonVisible = false,
            Color = Windows.UI.Color.FromArgb(255, color.Red, color.Green, color.Blue),
        };
        picker.ColorChanged += (_, e) =>
        {
            swatch.Fill = new SolidColorBrush(e.NewColor);
            onChange(new SKColor(e.NewColor.R, e.NewColor.G, e.NewColor.B));
            Refresh();
        };
        var button = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { swatch, new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center } },
            },
            Flyout = new Flyout { Content = picker },
        };
        _controls.Children.Add(button);
        return button;
    }

    public CheckBox AddCheck(string label, bool value, Action<bool> onChange)
    {
        var check = new CheckBox { Content = label, IsChecked = value, MinWidth = 0 };
        check.Click += (_, _) =>
        {
            onChange(check.IsChecked == true);
            Refresh();
        };
        _controls.Children.Add(check);
        return check;
    }

    public ComboBox AddChoice(string label, IReadOnlyList<string> items, int index, Action<int> onChange)
    {
        var combo = new ComboBox { Header = label, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var item in items)
        {
            combo.Items.Add(item);
        }
        combo.SelectedIndex = index;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                onChange(combo.SelectedIndex);
                Refresh();
            }
        };
        _controls.Children.Add(combo);
        return combo;
    }

    public TextBox AddText(string label, string text, Action<string> onChange)
    {
        var box = new TextBox { Header = label, Text = text };
        box.TextChanged += (_, _) =>
        {
            onChange(box.Text);
            Refresh();
        };
        _controls.Children.Add(box);
        return box;
    }

    public Button AddButton(string label, Func<Task> onClick)
    {
        var button = new Button { Content = label };
        button.Click += async (_, _) =>
        {
            await onClick();
            Refresh();
        };
        _controls.Children.Add(button);
        return button;
    }

    public TextBlock AddNote(string text)
    {
        var note = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 };
        _controls.Children.Add(note);
        return note;
    }

    // ---- Xem trước ----

    /// <summary>Tính lại ảnh xem trước: nền caro (thấy phần trong suốt), hiệu ứng trên ảnh nền thu nhỏ, rồi các hình đã vẽ
    /// (dời theo độ nới của ảnh).</summary>
    public void Refresh()
    {
        if (Effect is null)
        {
            return;
        }
        var result = Effect(_previewSource, _scale);
        using var composed = new SKBitmap(new SKImageInfo(result.Bitmap.Width, result.Bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(composed))
        {
            DrawChecker(canvas, composed.Width, composed.Height);
            canvas.DrawBitmap(result.Bitmap, 0, 0);
            canvas.Translate(result.Offset.X, result.Offset.Y);
            canvas.Scale(_scale);
            foreach (var shape in _shapes)
            {
                shape.Draw(canvas, _baseImage);
            }
        }
        result.Bitmap.Dispose();
        _preview.Source = composed.ToWriteableBitmap();
    }

    private static void DrawChecker(SKCanvas canvas, int width, int height)
    {
        const int cell = 12;
        using var paint = new SKPaint();
        for (int y = 0; y < height; y += cell)
        {
            for (int x = 0; x < width; x += cell)
            {
                paint.Color = (x / cell + y / cell) % 2 == 1 ? new SKColor(0xE6, 0xE6, 0xE6) : SKColors.White;
                canvas.DrawRect(x, y, cell, cell, paint);
            }
        }
    }
}
