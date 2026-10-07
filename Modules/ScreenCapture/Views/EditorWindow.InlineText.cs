using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ScreenCapture.Models;
using SkiaSharp;

namespace ScreenCapture.Views;

/// <summary>Gõ / sửa chữ ngay trên ảnh (Text, Khung chú thích) thay cho hộp thoại nhập chữ: 1 TextBox đặt đè đúng chỗ chữ,
/// cùng phông / cỡ (theo mức zoom) / màu; shape tạm ẩn phần chữ trong lúc gõ. Xong (Esc, Ctrl+Enter, bấm ra ngoài, đổi
/// công cụ / tab / zoom) thì ghi chữ vào shape thành 1 bước Undo.</summary>
public sealed partial class EditorWindow
{
    private TextBox? _inlineEditor;
    /// <summary>Shape đang gõ chữ (TextAnnotation / CalloutAnnotation).</summary>
    private AnnotationShape? _inlineTarget;
    /// <summary>Trạng thái shape trước khi sửa; null = chữ mới (chưa có trong ảnh, chỉ thêm vào khi gõ ít nhất 1 ký tự).</summary>
    private AnnotationShape? _inlineBefore;

    private static readonly string[] ForegroundKeys = ["TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused"];
    private static readonly string[] BackgroundKeys = ["TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused"];
    private static readonly string[] BorderKeys = ["TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused"];

    /// <summary>Bấm công cụ Text lên chỗ trống: chữ mới, dòng đầu canh giữa theo chiều dọc tại chỗ bấm.</summary>
    private void BeginNewText(SKPoint position)
    {
        var text = Styled(new TextAnnotation());
        text.FitBounds();
        var size = text.Bounds.Size;
        text.Bounds = SKRect.Create(position.X - text.Padding, position.Y - size.Height / 2, size.Width, size.Height);
        BeginInlineEdit(text);
    }

    private void BeginInlineEdit(AnnotationShape shape)
    {
        CommitInlineText();
        if (shape is not ITextShape textShape)
        {
            return;
        }
        _inlineTarget = shape;
        _inlineBefore = _viewModel.Annotations.Contains(shape) ? shape.Snapshot() : null;
        _viewModel.SelectedAnnotation = null;
        textShape.IsEditingText = true;

        // Toạ độ ảnh → DIP trong lớp InlineEditorHost (ngược với ToCanvasPoint).
        double scale = CurrentScale;
        double k = _zoom / scale;
        var r = shape.NormalizedBounds;
        float padding = shape switch
        {
            TextAnnotation t => t.Padding,
            CalloutAnnotation c => c.Padding,
            _ => 0,
        };
        var box = new TextBox
        {
            AcceptsReturn = true,
            IsSpellCheckEnabled = false,
            TextWrapping = shape is CalloutAnnotation or TextAnnotation { FixedWidth: true } ? TextWrapping.Wrap : TextWrapping.NoWrap,
            FontFamily = new FontFamily(textShape.FontFamily),
            FontSize = Math.Max(1, textShape.FontSize * k),
            FontWeight = textShape.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = textShape.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(Math.Max(0, padding * k - 1)),
            MinWidth = 24,
            MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness((r.Left * _zoom + _viewOrigin.X) / scale, (r.Top * _zoom + _viewOrigin.Y) / scale, 0, 0),
        };
        // Gán chữ SAU AcceptsReturn (ô 1 dòng cắt mất mọi thứ sau dấu xuống dòng đầu tiên); TextBox của WinUI xuống dòng
        // bằng '\r'.
        box.Text = textShape.Text.Replace("\n", "\r");
        if (shape is CalloutAnnotation or TextAnnotation { FixedWidth: true })
        {
            // Chữ tự xuống dòng đúng bề rộng khung (khung chú thích / chữ đã kéo khung); khung chú thích vẫn vẽ trên Canvas
            // phía dưới (ô nhập trong suốt).
            box.Width = Math.Max(24, r.Width * k);
            box.MinHeight = r.Height * k;
        }

        // Giữ đúng màu chữ / nền ở mọi trạng thái (theme mặc định đổi nền trắng + chữ đen khi ô nhập có focus).
        var foreground = new SolidColorBrush(ToWindowsColor(shape.Color));
        var background = new SolidColorBrush(shape is TextAnnotation { BackgroundColor: { } fill }
            ? ToWindowsColor(fill)
            : Microsoft.UI.Colors.Transparent);
        var border = new SolidColorBrush(Microsoft.UI.Colors.DeepSkyBlue);
        foreach (var key in ForegroundKeys)
        {
            box.Resources[key] = foreground;
        }
        foreach (var key in BackgroundKeys)
        {
            box.Resources[key] = background;
        }
        foreach (var key in BorderKeys)
        {
            box.Resources[key] = border;
        }

        box.PreviewKeyDown += InlineEditor_PreviewKeyDown;
        box.LostFocus += (_, _) =>
        {
            if (!ReferenceEquals(_inlineEditor, box))
            {
                return;
            }
            // Bấm lên ảnh để tạo / nhấp đúp sửa chữ: vùng cuộn quanh ảnh (IsTabStop) giành focus ngay sau khi ô nhập vừa
            // hiện → giữ ô nhập, lấy lại focus (bấm ra ngoài để xong đã xử lý ở Canvas_PointerPressed).
            var focused = FocusManager.GetFocusedElement(box.XamlRoot);
            if (focused is null || ReferenceEquals(focused, CanvasScroller))
            {
                box.Focus(FocusState.Programmatic);
                return;
            }
            // Bấm sang ribbon (đổi phông, màu...) → ghi chữ và chọn shape để định dạng áp lên nó.
            CommitInlineText();
        };
        box.Loaded += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.Select(box.Text.Length, 0);
        };

        _inlineEditor = box;
        InlineEditorHost.Children.Add(box);
        _viewModel.StatusText = "Gõ chữ: Enter = xuống dòng, Esc / Ctrl+Enter / bấm ra ngoài = xong.";
        // Đang gõ = đang sửa chữ đó → mở tab Định dạng với phông / cỡ / màu của nó (bấm control trên tab sẽ ghi chữ rồi
        // áp định dạng lên shape - xem LostFocus ở trên).
        UpdateFormatTab(switchToTab: true);
        Canvas.Invalidate();
    }

    private void InlineEditor_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape
            || (e.Key == Windows.System.VirtualKey.Enter && IsKeyDown(Windows.System.VirtualKey.Control)))
        {
            CommitInlineText();
            e.Handled = true;
        }
    }

    /// <summary>Kết thúc gõ chữ (không đang gõ thì không làm gì). Chữ mới rỗng → bỏ; chữ đã có bị xoá hết → xoá shape Text
    /// (khung chú thích rỗng vẫn giữ). <paramref name="select"/>: chọn shape sau khi xong - bấm ra chỗ trống trên ảnh thì
    /// không chọn, để lần bấm tiếp theo vẽ / gõ chữ mới luôn.</summary>
    private void CommitInlineText(bool select = true)
    {
        if (_inlineEditor is not { } box || _inlineTarget is not { } shape)
        {
            return;
        }
        _inlineEditor = null;
        _inlineTarget = null;
        bool hadFocus = box.FocusState != FocusState.Unfocused;
        InlineEditorHost.Children.Remove(box);
        // Xong bằng Esc / bấm lên ảnh: ô nhập đang giữ focus bị gỡ → đưa focus về vùng ảnh để phím tắt của Editor vẫn chạy.
        // Xong vì bấm sang control khác (ô phông, màu...) thì để focus ở control đó.
        if (hadFocus)
        {
            CanvasScroller.Focus(FocusState.Programmatic);
        }

        var textShape = (ITextShape)shape;
        textShape.IsEditingText = false;
        string text = box.Text.Replace("\r\n", "\n").Replace('\r', '\n');
        var before = _inlineBefore;
        _inlineBefore = null;

        if (before is null)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                textShape.Text = text;
                ((TextAnnotation)shape).FitBounds();
                _viewModel.AddAnnotation(shape);
                _viewModel.SelectedAnnotation = select ? shape : null;
            }
        }
        else if (text != textShape.Text)
        {
            if (shape is TextAnnotation && string.IsNullOrWhiteSpace(text))
            {
                _viewModel.RemoveAnnotation(shape);
            }
            else
            {
                textShape.Text = text;
                switch (shape)
                {
                    case TextAnnotation t:
                        t.FitBounds();
                        break;
                    case CalloutAnnotation c:
                        c.GrowToFitText();
                        break;
                }
                var after = shape.Snapshot();
                shape.RestoreFrom(before);
                _viewModel.ChangeShape(shape, before, after, "Sửa chữ");
                _viewModel.SelectedAnnotation = select ? shape : null;
            }
        }
        else
        {
            _viewModel.SelectedAnnotation = select ? shape : null;
        }
        // Xong mà không chọn shape (bấm chỗ trống, đổi công cụ...) → như bỏ chọn hình: về Trang chủ.
        if (_viewModel.SelectedAnnotation is null)
        {
            UpdateFormatTab();
            if (FormatTabHeader.IsChecked == true)
            {
                SelectRibbonTab(RibbonTab.Home);
            }
        }
        _viewModel.StatusText = "Sẵn sàng.";
        Canvas.Invalidate();
    }

    /// <summary>Nhấp đúp lên chữ / khung chú thích (mọi công cụ trừ Chọn vùng) = sửa chữ ngay trên ảnh.</summary>
    private void Canvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_inlineEditor is not null || _isCropping || _viewModel.SelectedTool == CaptureTool.Select)
        {
            return;
        }
        var pos = ToCanvasPoint(e.GetPosition(Canvas));
        var hit = _viewModel.Annotations.Reverse().FirstOrDefault(s => s is ITextShape && s.HitTest(pos, Px(6)));
        if (hit is not null)
        {
            _movingShape = null;
            _resizingHandle = -1;
            BeginInlineEdit(hit);
            e.Handled = true;
        }
    }
}
