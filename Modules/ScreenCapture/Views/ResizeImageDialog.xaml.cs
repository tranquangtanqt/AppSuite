using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ScreenCapture.Views;

/// <summary>Hộp thoại "Đổi cỡ ảnh" - xem ResizeImageDialog.xaml. Ô % và ô px luôn đồng bộ với nhau (sửa ô nào cũng
/// được); kết quả là <see cref="NewWidth"/> × <see cref="NewHeight"/> px.</summary>
public sealed partial class ResizeImageDialog : ContentDialog
{
    private readonly int _width;
    private readonly int _height;
    private bool _syncing;

    public ResizeImageDialog(int width, int height)
    {
        InitializeComponent();
        _width = width;
        _height = height;

        // Minimum/Maximum của NumberBox gán qua code - gán qua XAML attribute từng gây XamlParseException (xem EditorWindow).
        foreach (var box in new[] { WidthBox, HeightBox })
        {
            box.Minimum = 1;
            box.Maximum = NewImageDialog.MaxSide;
            box.SmallChange = 10;
            box.LargeChange = 100;
        }
        // % tối đa = đúng cạnh tối đa (px) theo từng chiều - không thì gõ px lớn, ô % bị kẹp ở 1000 trong khi px là 1638%.
        PercentXBox.Maximum = Math.Max(100, Math.Floor(NewImageDialog.MaxSide * 100.0 / width));
        PercentYBox.Maximum = Math.Max(100, Math.Floor(NewImageDialog.MaxSide * 100.0 / height));
        foreach (var box in new[] { PercentXBox, PercentYBox })
        {
            box.Minimum = 1;
            box.SmallChange = 5;
            box.LargeChange = 25;
        }

        _syncing = true;
        PercentXBox.Value = PercentYBox.Value = 100;
        WidthBox.Value = width;
        HeightBox.Value = height;
        _syncing = false;
        UpdateSummary();
    }

    public int NewWidth => (int)Math.Round(WidthBox.Value);
    public int NewHeight => (int)Math.Round(HeightBox.Value);

    private bool KeepRatio => KeepRatioBox.IsChecked == true;

    private void PercentBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncing || double.IsNaN(sender.Value))
        {
            UpdateSummary();
            return;
        }
        _syncing = true;
        bool horizontal = sender == PercentXBox;
        if (KeepRatio)
        {
            (horizontal ? PercentYBox : PercentXBox).Value = sender.Value;
        }
        if (horizontal || KeepRatio)
        {
            WidthBox.Value = Math.Max(1, Math.Round(_width * PercentXBox.Value / 100));
        }
        if (!horizontal || KeepRatio)
        {
            HeightBox.Value = Math.Max(1, Math.Round(_height * PercentYBox.Value / 100));
        }
        _syncing = false;
        UpdateSummary();
    }

    private void PixelBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncing || double.IsNaN(sender.Value))
        {
            UpdateSummary();
            return;
        }
        if (sender.Value != Math.Round(sender.Value))
        {
            sender.Value = Math.Round(sender.Value); // px nguyên - gọi lại handler này
            return;
        }
        _syncing = true;
        if (KeepRatio)
        {
            if (sender == WidthBox)
            {
                HeightBox.Value = Math.Max(1, Math.Round(WidthBox.Value * _height / _width));
            }
            else
            {
                WidthBox.Value = Math.Max(1, Math.Round(HeightBox.Value * _width / _height));
            }
        }
        SyncPercentFromPixels();
        _syncing = false;
        UpdateSummary();
    }

    /// <summary>Bật lại "Giữ tỉ lệ" → lấy chiều rộng làm chuẩn, tính lại chiều cao.</summary>
    private void KeepRatioBox_Click(object sender, RoutedEventArgs e)
    {
        if (!KeepRatio || double.IsNaN(WidthBox.Value))
        {
            return;
        }
        _syncing = true;
        HeightBox.Value = Math.Max(1, Math.Round(WidthBox.Value * _height / _width));
        SyncPercentFromPixels();
        _syncing = false;
        UpdateSummary();
    }

    private void SyncPercentFromPixels()
    {
        PercentXBox.Value = Math.Round(WidthBox.Value * 100.0 / _width, 1);
        PercentYBox.Value = Math.Round(HeightBox.Value * 100.0 / _height, 1);
    }

    private void UpdateSummary()
    {
        var error = NewImageDialog.SizeError(WidthBox.Value, HeightBox.Value);
        IsPrimaryButtonEnabled = error is null;
        SummaryText.Text = error ?? $"Ảnh {_width} × {_height} px → {NewWidth} × {NewHeight} px. Co giãn cả nội dung lẫn các hình đã vẽ.";
    }
}
