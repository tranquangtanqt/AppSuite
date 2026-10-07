using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScreenCapture.Models;
using ScreenCapture.Services;
using SkiaSharp;
using WinRT.Interop;

namespace ScreenCapture.Views;

/// <summary>Nút "Hiệu ứng" (nhóm Cắt &amp; Sửa): viền, đổ bóng, mép rách, độ sáng / tương phản, làm xám, sepia, đảo màu,
/// làm nét, watermark - áp lên ảnh nền của tab đang mở, 1 bước Undo. Tuỳ chọn lần trước được nhớ trong lúc app chạy.</summary>
public sealed partial class EditorWindow
{
    private static class EffectDefaults
    {
        public static float BorderWidth = 4;
        public static SKColor BorderColor = new(0x40, 0x40, 0x40);
        public static float ShadowSize = 14;
        public static float ShadowOpacity = 0.5f;
        public static TornSides TornSides = TornSides.Bottom;
        public static float TornDepth = 12;
        public static float Brightness;
        public static float Contrast;
        public static bool WatermarkUseImage;
        public static string WatermarkText = "BẢN NHÁP";
        public static string WatermarkFont = TextAnnotation.DefaultFontFamily;
        public static float WatermarkFontSize = 40;
        public static bool WatermarkBold = true;
        public static SKColor WatermarkColor = new(0x80, 0x80, 0x80);
        public static SKBitmap? WatermarkImage;
        public static string? WatermarkImageName;
        public static float WatermarkImageScale = 0.5f;
        public static float WatermarkOpacity = 0.3f;
        public static WatermarkPosition WatermarkPosition = WatermarkPosition.Tile;
    }

    private bool _effectDialogOpen;

    private void EffectItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }
        CommitInlineText(select: false);
        switch (tag)
        {
            case "Grayscale":
                ApplyQuickEffect(ImageEffects.Grayscale, "Làm xám");
                break;
            case "Sepia":
                ApplyQuickEffect(ImageEffects.Sepia, "Sepia");
                break;
            case "Invert":
                ApplyQuickEffect(ImageEffects.Invert, "Đảo màu");
                break;
            case "Sharpen":
                ApplyQuickEffect(b => ImageEffects.Sharpen(b), "Làm nét");
                break;
            case "Border":
                _ = ShowEffectDialogAsync("Viền ảnh", "Viền ảnh", dialog =>
                {
                    dialog.AddSlider("Độ dày", 1, 60, EffectDefaults.BorderWidth, 1, "{0:0} px", v => EffectDefaults.BorderWidth = (float)v);
                    dialog.AddColor("Màu viền", EffectDefaults.BorderColor, c => EffectDefaults.BorderColor = c);
                    dialog.AddNote("Ảnh to thêm 2 lần độ dày viền; các hình đã vẽ dời theo, vẫn nằm đúng chỗ.");
                }, (bitmap, scale) => ImageEffects.Border(bitmap, EffectDefaults.BorderWidth, EffectDefaults.BorderColor, scale));
                break;
            case "Shadow":
                _ = ShowEffectDialogAsync("Đổ bóng", "Đổ bóng", dialog =>
                {
                    dialog.AddSlider("Độ lan", 2, 60, EffectDefaults.ShadowSize, 1, "{0:0} px", v => EffectDefaults.ShadowSize = (float)v);
                    dialog.AddSlider("Độ đậm", 10, 100, EffectDefaults.ShadowOpacity * 100, 5, "{0:0}%", v => EffectDefaults.ShadowOpacity = (float)v / 100);
                    dialog.AddNote("Bóng đổ xuống dưới-phải trên nền trong suốt: lưu PNG giữ nền trong suốt, JPG / BMP nền trắng. Làm Mép rách trước rồi Đổ bóng thì bóng theo đúng mép rách.");
                }, (bitmap, scale) => ImageEffects.Shadow(bitmap, EffectDefaults.ShadowSize, EffectDefaults.ShadowOpacity, scale));
                break;
            case "Torn":
                _ = ShowEffectDialogAsync("Mép rách", "Mép rách", dialog =>
                {
                    foreach (var (side, label) in new[] { (TornSides.Top, "Cạnh trên"), (TornSides.Bottom, "Cạnh dưới"), (TornSides.Left, "Cạnh trái"), (TornSides.Right, "Cạnh phải") })
                    {
                        dialog.AddCheck(label, EffectDefaults.TornSides.HasFlag(side),
                            on => EffectDefaults.TornSides = on ? EffectDefaults.TornSides | side : EffectDefaults.TornSides & ~side);
                    }
                    dialog.AddSlider("Độ sâu răng cưa", 3, 40, EffectDefaults.TornDepth, 1, "{0:0} px", v => EffectDefaults.TornDepth = (float)v);
                    dialog.AddNote("Phần bị xé thành trong suốt, cỡ ảnh giữ nguyên.");
                }, (bitmap, scale) => ImageEffects.TornEdge(bitmap, EffectDefaults.TornSides, EffectDefaults.TornDepth, scale));
                break;
            case "Brightness":
                _ = ShowEffectDialogAsync("Độ sáng / tương phản", "Độ sáng / tương phản", dialog =>
                {
                    dialog.AddSlider("Độ sáng", -100, 100, EffectDefaults.Brightness, 1, "{0:+0;-0;0}", v => EffectDefaults.Brightness = (float)v);
                    dialog.AddSlider("Tương phản", -100, 100, EffectDefaults.Contrast, 1, "{0:+0;-0;0}", v => EffectDefaults.Contrast = (float)v);
                }, (bitmap, _) => new EffectResult(ImageEffects.BrightnessContrast(bitmap, EffectDefaults.Brightness, EffectDefaults.Contrast), default));
                break;
            case "Watermark":
                _ = ShowEffectDialogAsync("Watermark", "Watermark", BuildWatermarkDialog,
                    (bitmap, scale) => new EffectResult(ImageEffects.Watermark(bitmap, CurrentWatermark(), scale), default));
                break;
        }
    }

    private static WatermarkSettings CurrentWatermark() => new(
        EffectDefaults.WatermarkText, EffectDefaults.WatermarkFont, EffectDefaults.WatermarkFontSize, EffectDefaults.WatermarkBold,
        EffectDefaults.WatermarkColor, EffectDefaults.WatermarkUseImage ? EffectDefaults.WatermarkImage : null,
        EffectDefaults.WatermarkImageScale, EffectDefaults.WatermarkOpacity, EffectDefaults.WatermarkPosition);

    private void BuildWatermarkDialog(ImageEffectDialog dialog)
    {
        dialog.AddChoice("Loại", ["Chữ", "Ảnh (logo…)"], EffectDefaults.WatermarkUseImage ? 1 : 0, i => EffectDefaults.WatermarkUseImage = i == 1);
        dialog.AddText("Chữ", EffectDefaults.WatermarkText, t => EffectDefaults.WatermarkText = t);
        dialog.AddSlider("Cỡ chữ", 10, 200, EffectDefaults.WatermarkFontSize, 2, "{0:0} px", v => EffectDefaults.WatermarkFontSize = (float)v);
        dialog.AddCheck("Chữ đậm", EffectDefaults.WatermarkBold, on => EffectDefaults.WatermarkBold = on);
        dialog.AddColor("Màu chữ", EffectDefaults.WatermarkColor, c => EffectDefaults.WatermarkColor = c);
        dialog.AddColumnBreak();
        TextBlock? imageNote = null;
        dialog.AddButton("Chọn ảnh…", async () =>
        {
            var paths = await _fileService.PickImagesAsync(WindowNative.GetWindowHandle(this));
            var (images, _) = await ImageFileService.LoadImagesAsync(paths.Take(1));
            if (images.Count > 0)
            {
                EffectDefaults.WatermarkImage = images[0].Bitmap;
                EffectDefaults.WatermarkImageName = images[0].Name;
                EffectDefaults.WatermarkUseImage = true;
                imageNote!.Text = $"Ảnh: {images[0].Name} ({images[0].Bitmap.Width} × {images[0].Bitmap.Height} px)";
            }
        });
        imageNote = dialog.AddNote(EffectDefaults.WatermarkImageName is { } name ? $"Ảnh: {name}" : "Chưa chọn ảnh (Loại = Ảnh mà chưa chọn thì dùng chữ).");
        dialog.AddSlider("Cỡ ảnh", 5, 200, EffectDefaults.WatermarkImageScale * 100, 5, "{0:0}%", v => EffectDefaults.WatermarkImageScale = (float)v / 100);
        dialog.AddSlider("Độ đục", 5, 100, EffectDefaults.WatermarkOpacity * 100, 5, "{0:0}%", v => EffectDefaults.WatermarkOpacity = (float)v / 100);
        dialog.AddChoice("Vị trí", ["Giữa", "Góc trên-trái", "Góc trên-phải", "Góc dưới-trái", "Góc dưới-phải", "Lặp kín ảnh (chéo)"],
            (int)EffectDefaults.WatermarkPosition, i => EffectDefaults.WatermarkPosition = (WatermarkPosition)i);
    }

    /// <summary>Hiệu ứng không có tuỳ chọn: áp luôn (Undo được).</summary>
    private void ApplyQuickEffect(Func<SKBitmap, SKBitmap> effect, string description)
    {
        try
        {
            _viewModel.ApplyEffect(new EffectResult(effect(_viewModel.Bitmap), default), description);
            Canvas.Invalidate();
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Không áp được hiệu ứng {Effect}", description);
            _viewModel.StatusText = $"Không áp được hiệu ứng {description}: {ex.Message}";
        }
    }

    /// <summary>Mở hộp thoại tuỳ chọn + xem trước; bấm Áp dụng → áp hiệu ứng lên ảnh thật của tab đang mở.</summary>
    private async Task ShowEffectDialogAsync(string title, string description, Action<ImageEffectDialog> build,
        Func<SKBitmap, float, EffectResult> effect)
    {
        if (_effectDialogOpen)
        {
            return; // ContentDialog thứ 2 cùng lúc sẽ ném lỗi
        }
        _effectDialogOpen = true;
        try
        {
            var vm = _viewModel;
            var dialog = new ImageEffectDialog(title, vm.Bitmap, vm.Annotations.ToList()) { XamlRoot = Content.XamlRoot };
            build(dialog);
            dialog.Effect = effect;
            dialog.Refresh();
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                vm.ApplyEffect(effect(vm.Bitmap, 1), description);
                Canvas.Invalidate();
            }
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Không áp được hiệu ứng {Effect}", description);
            _viewModel.StatusText = $"Không áp được hiệu ứng {description}: {ex.Message}";
        }
        finally
        {
            _effectDialogOpen = false;
        }
    }
}
