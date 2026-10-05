using SkiaSharp;

namespace ScreenCapture.Models;

public enum ImageTransformKind
{
    RotateRight,
    RotateLeft,
    Rotate180,
    FlipHorizontal,
    FlipVertical,
    Resize,
}

/// <summary>Xoay 90° / 180°, lật ngang / dọc hoặc co giãn cả ảnh (khác đổi khung ảnh - <see cref="Commands.ResizeCanvasCommand"/>
/// chỉ nới / cắt khung, không co giãn nội dung). Áp lên ảnh nền và tạo BẢN SAO đã biến đổi của từng shape - shape vẫn
/// chọn / sửa được sau đó, còn shape cũ giữ nguyên cho Undo (các bước Undo trước đó vẫn trỏ tới đúng shape cũ).
///
/// Text và Stamp giữ chiều đứng (không có thuộc tính góc xoay), chỉ dời theo tâm; stamp mũi tên đổi hướng cho khớp ảnh.</summary>
public sealed class ImageTransform
{
    private readonly SKMatrix _matrix;

    private ImageTransform(ImageTransformKind kind, int oldWidth, int oldHeight, int newWidth, int newHeight)
    {
        Kind = kind;
        NewWidth = newWidth;
        NewHeight = newHeight;
        float w = oldWidth, h = oldHeight;
        // Ánh xạ toạ độ pixel ảnh cũ → ảnh mới (toạ độ là cạnh pixel, nên dùng W/H chứ không phải W-1/H-1).
        _matrix = kind switch
        {
            ImageTransformKind.RotateRight => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),   // (x, y) → (H - y, x)
            ImageTransformKind.RotateLeft => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),    // (x, y) → (y, W - x)
            ImageTransformKind.Rotate180 => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),    // (x, y) → (W - x, H - y)
            ImageTransformKind.FlipHorizontal => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1), // (x, y) → (W - x, y)
            ImageTransformKind.FlipVertical => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),   // (x, y) → (x, H - y)
            _ => SKMatrix.CreateScale((float)newWidth / oldWidth, (float)newHeight / oldHeight),
        };
        ScaleX = kind == ImageTransformKind.Resize ? (float)newWidth / oldWidth : 1;
        ScaleY = kind == ImageTransformKind.Resize ? (float)newHeight / oldHeight : 1;
    }

    public ImageTransformKind Kind { get; }
    public int NewWidth { get; }
    public int NewHeight { get; }
    public float ScaleX { get; }
    public float ScaleY { get; }

    /// <summary>Hệ số cho nét vẽ / cỡ chữ / cỡ stamp khi co giãn (trung bình 2 chiều - co giãn không đều thì nét không méo).</summary>
    private float LengthScale => (ScaleX + ScaleY) / 2;

    public static ImageTransform Create(ImageTransformKind kind, int width, int height)
    {
        if (kind == ImageTransformKind.Resize)
        {
            throw new ArgumentException("Dùng CreateResize cho co giãn ảnh.", nameof(kind));
        }
        bool swap = kind is ImageTransformKind.RotateRight or ImageTransformKind.RotateLeft;
        return new ImageTransform(kind, width, height, swap ? height : width, swap ? width : height);
    }

    public static ImageTransform CreateResize(int width, int height, int newWidth, int newHeight) =>
        new(ImageTransformKind.Resize, width, height, Math.Max(1, newWidth), Math.Max(1, newHeight));

    public string Description => Kind switch
    {
        ImageTransformKind.RotateRight => "Xoay phải 90°",
        ImageTransformKind.RotateLeft => "Xoay trái 90°",
        ImageTransformKind.Rotate180 => "Xoay 180°",
        ImageTransformKind.FlipHorizontal => "Lật ngang",
        ImageTransformKind.FlipVertical => "Lật dọc",
        _ => "Đổi cỡ ảnh",
    };

    public SKPoint MapPoint(SKPoint p) => _matrix.MapPoint(p);

    /// <summary>Khung đã biến đổi, chuẩn hoá (Left &lt;= Right, Top &lt;= Bottom).</summary>
    public SKRect MapRect(SKRect r) => _matrix.MapRect(r).Standardized;

    /// <summary>Ảnh mới cùng định dạng pixel với ảnh chụp. Xoay / lật: chép đúng từng pixel (không nội suy);
    /// co giãn: lọc chất lượng cao (thu nhỏ dùng mipmap, không bị răng cưa).</summary>
    public SKBitmap Apply(SKBitmap source)
    {
        var info = new SKImageInfo(NewWidth, NewHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        if (Kind == ImageTransformKind.Resize)
        {
            using var converted = source.ColorType == SKColorType.Bgra8888 ? null : source.Copy(SKColorType.Bgra8888);
            var resized = (converted ?? source).Resize(info, SKFilterQuality.High);
            if (resized is not null)
            {
                return resized;
            }
        }
        var result = new SKBitmap(info);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        canvas.SetMatrix(_matrix);
        using var paint = new SKPaint { FilterQuality = Kind == ImageTransformKind.Resize ? SKFilterQuality.High : SKFilterQuality.None };
        canvas.DrawBitmap(source, 0, 0, paint);
        return result;
    }

    /// <summary>Bản sao đã biến đổi của <paramref name="shape"/>; null nếu là loại shape chưa biết (bên gọi giữ nguyên).</summary>
    public AnnotationShape? Transform(AnnotationShape shape)
    {
        AnnotationShape? copy = shape switch
        {
            RectangleAnnotation => new RectangleAnnotation { Bounds = MapRect(shape.Bounds) },
            EllipseAnnotation => new EllipseAnnotation { Bounds = MapRect(shape.Bounds) },
            HighlightAnnotation => new HighlightAnnotation { Bounds = MapRect(shape.Bounds) },
            RedactAnnotation redact => new RedactAnnotation { Mode = redact.Mode, Bounds = MapRect(shape.Bounds) },
            LineArrowAnnotation line => new LineArrowAnnotation
            {
                IsArrow = line.IsArrow,
                Bounds = LineArrowAnnotation.FromPoints(MapPoint(line.Start), MapPoint(line.End)),
            },
            FreehandAnnotation freehand => TransformFreehand(freehand),
            ImageAnnotation image => new ImageAnnotation(Kind == ImageTransformKind.Resize ? image.Image : Apply(image.Image, Kind))
            {
                Bounds = MapRect(shape.Bounds),
            },
            TextAnnotation text => TransformText(text),
            StampAnnotation stamp => new StampAnnotation
            {
                Kind = MapStampKind(stamp.Kind),
                NumberValue = stamp.NumberValue,
                OutlineColor = stamp.OutlineColor,
                Bounds = CenteredAt(MapPoint(new SKPoint(stamp.NormalizedBounds.MidX, stamp.NormalizedBounds.MidY)),
                    stamp.NormalizedBounds.Width * LengthScale, stamp.NormalizedBounds.Height * LengthScale),
            },
            _ => null,
        };
        if (copy is null)
        {
            return null;
        }
        copy.Color = shape.Color;
        // Mosaic / Blur: StrokeWidth là mức độ che, không phải độ dày nét → giữ nguyên.
        copy.StrokeWidth = shape is RedactAnnotation ? shape.StrokeWidth : shape.StrokeWidth * LengthScale;
        return copy;
    }

    /// <summary>Ảnh dán (ImageAnnotation) xoay / lật cùng chiều với ảnh nền.</summary>
    private static SKBitmap Apply(SKBitmap image, ImageTransformKind kind) => Create(kind, image.Width, image.Height).Apply(image);

    private FreehandAnnotation TransformFreehand(FreehandAnnotation freehand)
    {
        var copy = new FreehandAnnotation();
        copy.SetPoints(freehand.CurrentPoints().Select(MapPoint));
        copy.Bounds = copy.PointsBounds;
        return copy;
    }

    /// <summary>Chữ vẽ từ góc trên-trái Bounds (Bounds không theo đúng cỡ chữ) → lấy tâm của phần chữ thật sự, dời tâm đó
    /// theo ảnh rồi đặt chữ (vẫn nằm ngang) quanh tâm mới.</summary>
    private TextAnnotation TransformText(TextAnnotation text)
    {
        using var paint = new SKPaint { Typeface = SKTypeface.Default, TextSize = text.FontSize };
        float width = paint.MeasureText(text.Text);
        float height = text.FontSize * 1.2f;
        var center = MapPoint(new SKPoint(text.Bounds.Left + width / 2, text.Bounds.Top + height / 2));
        float fontSize = text.FontSize * LengthScale;
        var size = new SKSize(text.Bounds.Width * LengthScale, text.Bounds.Height * LengthScale);
        float left = center.X - width * LengthScale / 2, top = center.Y - fontSize * 1.2f / 2;
        return new TextAnnotation
        {
            Text = text.Text,
            FontSize = fontSize,
            Bounds = SKRect.Create(left, top, size.Width, size.Height),
        };
    }

    private static SKRect CenteredAt(SKPoint center, float width, float height) =>
        SKRect.Create(center.X - width / 2, center.Y - height / 2, width, height);

    /// <summary>Hướng mũi tên của stamp sau khi biến đổi ảnh. Đi theo vòng 8 hướng (theo chiều kim đồng hồ, bắt đầu từ Lên).</summary>
    private StampKind MapStampKind(StampKind kind)
    {
        int index = Array.IndexOf(ArrowRing, kind);
        if (index < 0)
        {
            return kind;
        }
        // Lật ngang: hướng i ↔ -i (Lên giữ Lên, Phải ↔ Trái); lật dọc: i ↔ 4 - i (Lên ↔ Xuống).
        int mapped = Kind switch
        {
            ImageTransformKind.RotateRight => index + 2,
            ImageTransformKind.RotateLeft => index - 2,
            ImageTransformKind.Rotate180 => index + 4,
            ImageTransformKind.FlipHorizontal => -index,
            ImageTransformKind.FlipVertical => 4 - index,
            _ => index,
        };
        return ArrowRing[((mapped % 8) + 8) % 8];
    }

    private static readonly StampKind[] ArrowRing =
    [
        StampKind.ArrowUp, StampKind.ArrowUpRight, StampKind.ArrowRight, StampKind.ArrowDownRight,
        StampKind.ArrowDown, StampKind.ArrowDownLeft, StampKind.ArrowLeft, StampKind.ArrowUpLeft,
    ];
}
