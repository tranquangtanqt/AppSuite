using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace MeRon.Views;

/// <summary>1 chuỗi số liệu: tên (chú thích) + màu nền sáng / nền tối (đã kiểm bằng validate_palette của skill dataviz).</summary>
public sealed record ChartSeries(string Name, Color Light, Color Dark);

/// <summary>1 cột: nhãn trục (ngắn), giá trị từng chuỗi (cột chồng: dưới → trên), chữ hiện khi rê chuột / đọc màn hình.</summary>
public sealed record ChartColumn(string AxisLabel, double[] Values, string Tooltip);

/// <summary>1 thanh ngang: tên, giá trị, chữ ở đầu thanh, chữ khi rê chuột.</summary>
public sealed record ChartBar(string Name, double Value, string ValueText, string Tooltip);

/// <summary>Màu dùng chung cho biểu đồ (palette mặc định của skill dataviz + cam chủ đạo của app).</summary>
public static class ChartColors
{
    /// <summary>Doanh thu / bán: cam chủ đạo #E8590C (đạt mọi kiểm tra cả nền sáng và tối).</summary>
    public static readonly ChartSeries Sales = new("Doanh thu", Hex(0xE8590C), Hex(0xE8590C));

    /// <summary>Chuỗi thứ 1 / 2 của palette phân loại (xanh dương, cam) - cặp đạt kiểm tra mù màu ΔE 24.7 sáng / 26.8 tối.</summary>
    public static readonly (Color Light, Color Dark) Slot1 = (Hex(0x2A78D6), Hex(0x3987E5));
    public static readonly (Color Light, Color Dark) Slot2 = (Hex(0xEB6834), Hex(0xD95926));

    public static Color Hex(uint rgb) => Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static Brush Theme(string key) => (Brush)Application.Current.Resources[key];
}

/// <summary>
/// Biểu đồ cột (1 chuỗi hoặc cột chồng) vẽ bằng phần tử XAML - theo quy cách của skill dataviz: cột ≤ 24px, đầu dữ liệu bo
/// 4px, chân vuông trên 1 đường gốc; khe 2px giữa các đoạn chồng; lưới 1px liền màu chìm; số trục làm tròn, rút gọn
/// (500k, 1,2tr); chỉ ghi số ở cột cao nhất; rê chuột cả dải cột (rộng hơn cột) hiện chi tiết; ≥ 2 chuỗi thì có chú thích.
/// Mỗi dải là 1 Button trong suốt (không Tab) mang tên = chi tiết để trình đọc màn hình / UI Automation đọc được.
/// </summary>
public sealed class ColumnChart : Grid
{
    private const double TickWidth = 56, AxisHeight = 22, TopSpace = 20, BarMax = 24;
    private readonly Canvas _canvas = new();
    private readonly StackPanel _legend = new() { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(TickWidth, 0, 0, 8) };
    private IReadOnlyList<ChartColumn> _columns = [];
    private IReadOnlyList<ChartSeries> _series = [ChartColors.Sales];

    public ColumnChart()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        SetRow(_canvas, 1);
        Children.Add(_legend);
        Children.Add(_canvas);
        Height = 260;
        SizeChanged += (_, _) => Draw();
        ActualThemeChanged += (_, _) => Draw();
    }

    /// <summary>Định dạng số trên trục / nhãn cột cao nhất (vd tiền rút gọn).</summary>
    public Func<double, string> Format { get; set; } = v => Core.VnFormat.Number((long)v);

    public IReadOnlyList<ChartSeries> Series
    {
        get => _series;
        set { _series = value; Draw(); }
    }

    public IReadOnlyList<ChartColumn> Columns
    {
        get => _columns;
        set { _columns = value; Draw(); }
    }

    private Brush SeriesBrush(int i) => new SolidColorBrush(ActualTheme == ElementTheme.Dark ? _series[i].Dark : _series[i].Light);

    private void Draw()
    {
        _canvas.Children.Clear();
        _legend.Children.Clear();
        _legend.Visibility = _series.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        for (var i = 0; i < _series.Count && _series.Count >= 2; i++)
        {
            var key = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            key.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = SeriesBrush(i), VerticalAlignment = VerticalAlignment.Center });
            key.Children.Add(new TextBlock { Text = _series[i].Name, FontSize = 12, Foreground = ChartColors.Theme("TextFillColorSecondaryBrush") });
            _legend.Children.Add(key);
        }

        var width = _canvas.ActualWidth;
        var height = _canvas.ActualHeight;
        if (_columns.Count == 0 || width < TickWidth + 20 || height < AxisHeight + TopSpace + 20)
        {
            return;
        }
        var plotLeft = TickWidth;
        var plotWidth = width - plotLeft;
        var plotTop = TopSpace;
        var plotHeight = height - AxisHeight - TopSpace;
        var baseline = plotTop + plotHeight;
        var max = _columns.Max(c => c.Values.Sum());
        var (top, step) = NiceScale(max);
        double Y(double value) => baseline - value / top * plotHeight;

        var grid = ChartColors.Theme("DividerStrokeColorDefaultBrush");
        var muted = ChartColors.Theme("TextFillColorSecondaryBrush");
        for (var v = 0.0; v <= top + step / 2; v += step)
        {
            var y = Math.Round(Y(v)) + 0.5;
            _canvas.Children.Add(new Line { X1 = plotLeft, X2 = width, Y1 = y, Y2 = y, Stroke = grid, StrokeThickness = 1 });
            var tick = new TextBlock { Text = Format(v), FontSize = 11, Foreground = muted, Width = TickWidth - 8, TextAlignment = TextAlignment.Right };
            Canvas.SetLeft(tick, 0);
            Canvas.SetTop(tick, y - 8);
            _canvas.Children.Add(tick);
        }

        var slot = plotWidth / _columns.Count;
        var barWidth = Math.Max(2, Math.Min(BarMax, slot * 0.7));
        var labelEvery = Math.Max(1, (int)Math.Ceiling(_columns.Count * 46 / plotWidth));
        var maxIndex = _columns.Select((c, i) => (Total: c.Values.Sum(), i)).MaxBy(t => t.Total).i;
        for (var i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i];
            var x = plotLeft + i * slot;

            // Dải bắt chuột (cả chiều cao, rộng = cả ô) - nằm dưới cột; cột không bắt chuột.
            var hit = new Button
            {
                Width = slot, Height = plotHeight + AxisHeight, Padding = new Thickness(0), BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent), IsTabStop = false, CornerRadius = new CornerRadius(4),
            };
            ToolTipService.SetToolTip(hit, column.Tooltip);
            AutomationProperties.SetName(hit, column.Tooltip);
            Canvas.SetLeft(hit, x);
            Canvas.SetTop(hit, plotTop);
            _canvas.Children.Add(hit);

            var barLeft = x + (slot - barWidth) / 2;
            var bottom = baseline;
            var topIndex = Array.FindLastIndex(column.Values, v => v > 0);
            for (var s = 0; s < column.Values.Length && s < _series.Count; s++)
            {
                var value = column.Values[s];
                if (value <= 0)
                {
                    continue;
                }
                var segment = Math.Max(1, baseline - Y(value));
                var gap = bottom < baseline ? 2 : 0; // khe 2px màu nền giữa các đoạn chồng
                var isTop = s == topIndex;
                var rect = new Border
                {
                    Width = barWidth, Height = Math.Max(1, segment - gap), Background = SeriesBrush(s), IsHitTestVisible = false,
                    CornerRadius = isTop && barWidth >= 8 ? new CornerRadius(4, 4, 0, 0) : new CornerRadius(0),
                };
                Canvas.SetLeft(rect, barLeft);
                Canvas.SetTop(rect, bottom - segment);
                _canvas.Children.Add(rect);
                bottom -= segment;
            }

            if (i % labelEvery == 0)
            {
                var label = new TextBlock { Text = column.AxisLabel, FontSize = 11, Foreground = muted, Width = slot * labelEvery, TextAlignment = TextAlignment.Center, IsHitTestVisible = false };
                Canvas.SetLeft(label, x + slot / 2 - slot * labelEvery / 2);
                Canvas.SetTop(label, baseline + 4);
                _canvas.Children.Add(label);
            }
            if (i == maxIndex && column.Values.Sum() > 0)
            {
                var value = new TextBlock { Text = Format(column.Values.Sum()), FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, IsHitTestVisible = false };
                value.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(value, Math.Clamp(x + slot / 2 - value.DesiredSize.Width / 2, plotLeft, width - value.DesiredSize.Width));
                Canvas.SetTop(value, Y(column.Values.Sum()) - 17);
                _canvas.Children.Add(value);
            }
        }
        // Đường gốc
        _canvas.Children.Add(new Line { X1 = plotLeft, X2 = width, Y1 = Math.Round(baseline) + 0.5, Y2 = Math.Round(baseline) + 0.5, Stroke = muted, StrokeThickness = 1, Opacity = 0.5 });
    }

    /// <summary>Trục tròn số: bước 1 / 2 / 2,5 / 5 × 10ⁿ, khoảng 4 vạch.</summary>
    internal static (double Top, double Step) NiceScale(double max)
    {
        if (max <= 0)
        {
            return (4, 1);
        }
        var raw = max / 4;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var step = new[] { 1, 2, 2.5, 5, 10 }.Select(f => f * magnitude).First(s => s >= raw);
        return (Math.Ceiling(max / step) * step, step);
    }
}

/// <summary>Thanh ngang xếp từ lớn tới nhỏ (vd món bán chạy): tên bên trái, thanh 1 màu (đầu dữ liệu bo 4px, gốc vuông),
/// số ở đầu thanh; rê chuột cả dòng hiện chi tiết.</summary>
public sealed class BarList : StackPanel
{
    private IReadOnlyList<ChartBar> _bars = [];

    public BarList()
    {
        Spacing = 6;
        ActualThemeChanged += (_, _) => Draw();
    }

    public ChartSeries Series { get; set; } = ChartColors.Sales;

    public IReadOnlyList<ChartBar> Bars
    {
        get => _bars;
        set { _bars = value; Draw(); }
    }

    private void Draw()
    {
        Children.Clear();
        var max = _bars.Count > 0 ? _bars.Max(b => b.Value) : 0;
        var brush = new SolidColorBrush(ActualTheme == ElementTheme.Dark ? Series.Dark : Series.Light);
        foreach (var bar in _bars)
        {
            var row = new Grid { ColumnSpacing = 12, Background = new SolidColorBrush(Colors.Transparent) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = bar.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });

            var track = new Grid();
            var share = max > 0 ? bar.Value / max : 0;
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(share, 0.002), GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - share, 0.0001) + 0.25, GridUnitType.Star) });
            track.Children.Add(new Border { Height = 16, Background = brush, CornerRadius = new CornerRadius(0, 4, 4, 0) });
            var value = new TextBlock { Text = bar.ValueText, FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(value, 1);
            track.Children.Add(value);
            Grid.SetColumn(track, 1);
            row.Children.Add(track);

            ToolTipService.SetToolTip(row, bar.Tooltip);
            AutomationProperties.SetName(row, bar.Tooltip);
            Children.Add(row);
        }
    }
}

/// <summary>Bảng số liệu dưới biểu đồ (bật "Bảng số liệu") - cùng dữ liệu, cho người không đọc được màu / cần số chính xác.</summary>
public sealed class ChartTable : Grid
{
    public void Show(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        Children.Clear();
        ColumnDefinitions.Clear();
        RowDefinitions.Clear();
        ColumnSpacing = 16;
        for (var c = 0; c < headers.Count; c++)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = c == 0 ? new GridLength(1.4, GridUnitType.Star) : new GridLength(1, GridUnitType.Star) });
        }
        void Add(string text, int row, int col, bool header)
        {
            var cell = new TextBlock
            {
                Text = text, FontSize = 12, Margin = new Thickness(0, 2, 0, 2), IsTextSelectionEnabled = !header,
                FontWeight = header ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                Opacity = header ? 0.75 : 1, TextAlignment = col == 0 ? TextAlignment.Left : TextAlignment.Right,
            };
            SetRow(cell, row);
            SetColumn(cell, col);
            Children.Add(cell);
        }
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var c = 0; c < headers.Count; c++)
        {
            Add(headers[c], 0, c, true);
        }
        for (var r = 0; r < rows.Count; r++)
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var c = 0; c < rows[r].Length && c < headers.Count; c++)
            {
                Add(rows[r][c], r + 1, c, false);
            }
        }
    }
}
