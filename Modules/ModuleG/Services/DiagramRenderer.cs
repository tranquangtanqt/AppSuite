using SkiaSharp;

namespace ModuleG.Services;

/// <summary>
/// Draws the box/connector shapes <see cref="DiagramXmlReader"/> pulled out of the raw DrawingML XML
/// (the 処理関連図/サービス関連図 flow diagram on the 概要 sheet) to a PNG with SkiaSharp - entirely
/// in-process, no Excel installation required on the machine running the import.
///
/// Connector routing: `straightConnector1` is a direct line between its 2 bounding-box corners.
/// `bentConnectorN` (the box-to-box elbow arrows this diagram mostly uses) is approximated as a single
/// horizontal-then-vertical-then-horizontal "Z" bend at the `adj1` guide fraction along the width
/// (`bentConnector3`'s own documented DrawingML formula - see ECMA-376 Part 1 §20.1.9.18) or, for the
/// no-adjustment `bentConnector2` variant, a fixed corner at the shape's top-right. Anything else
/// (`bentConnector4/5` with more bends, or an unrecognized preset) falls back to a straight line rather
/// than guessing wrong - a straight line still shows which boxes connect, just not the exact elbow
/// route, consistent with this codebase's "never lose the content, degrade the polish" grid fallbacks.
/// Box presets (`rect`/`flowChartProcess`/`flowChartPredefinedProcess`/anything else) are all drawn as
/// the same plain filled+bordered rectangle - not pixel-perfect vs. Excel's own flowchart glyphs, but
/// faithful to position/color/text/size, which is what actually carries the diagram's meaning.
/// </summary>
internal static class DiagramRenderer
{
    private const double EmuPerPixel = 914400.0 / 96.0;
    private const float Padding = 16f;
    private const float FontSizePx = 12f;

    public static bool TryRender(IReadOnlyList<DiagramShape> shapes, string outputPngPath, out string? error)
    {
        if (shapes.Count == 0)
        {
            error = "Khong tim thay shape nao trong vung nay (co the la marker khong co so do that su).";
            return false;
        }

        var minX = shapes.Min(s => s.X);
        var minY = shapes.Min(s => s.Y);
        var maxX = shapes.Max(s => s.X + s.Width);
        var maxY = shapes.Max(s => s.Y + s.Height);

        var widthPx = (int)Math.Max(1, Math.Ceiling((maxX - minX) / EmuPerPixel + Padding * 2));
        var heightPx = (int)Math.Max(1, Math.Ceiling((maxY - minY) / EmuPerPixel + Padding * 2));

        try
        {
            using var bitmap = new SKBitmap(widthPx, heightPx);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);

            float ToPxX(double emu) => (float)((emu - minX) / EmuPerPixel + Padding);
            float ToPxY(double emu) => (float)((emu - minY) / EmuPerPixel + Padding);

            using var typeface = SKTypeface.FromFamilyName("Yu Gothic UI") ?? SKTypeface.Default;
            using var textPaint = new SKPaint
            {
                Color = SKColors.Black,
                IsAntialias = true,
                TextAlign = SKTextAlign.Center,
                Typeface = typeface,
                TextSize = FontSizePx,
            };

            // Connectors drawn first so box fills sit on top of any line endpoint that lands inside one.
            foreach (var shape in shapes.Where(s => s.IsConnector))
            {
                DrawConnector(canvas, shape, ToPxX, ToPxY);
            }

            foreach (var shape in shapes.Where(s => !s.IsConnector))
            {
                DrawBox(canvas, shape, ToPxX, ToPxY, textPaint);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPngPath)!);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(outputPngPath);
            data.SaveTo(stream);

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void DrawBox(SKCanvas canvas, DiagramShape shape, Func<double, float> toPxX, Func<double, float> toPxY, SKPaint textPaintTemplate)
    {
        var rect = new SKRect(toPxX(shape.X), toPxY(shape.Y), toPxX(shape.X + shape.Width), toPxY(shape.Y + shape.Height));

        if (shape.FillColorArgb is { } fill)
        {
            using var fillPaint = new SKPaint { Color = new SKColor(fill), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRect(rect, fillPaint);
        }

        if (shape.LineWidthEmu > 0 && shape.LineColorArgb is { } line)
        {
            using var strokePaint = new SKPaint
            {
                Color = new SKColor(line),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)(shape.LineWidthEmu / EmuPerPixel),
                IsAntialias = true,
            };
            canvas.DrawRect(rect, strokePaint);
        }

        if (string.IsNullOrEmpty(shape.Text))
        {
            return;
        }

        var lines = shape.Text.Split('\n');
        var lineHeight = textPaintTemplate.TextSize * 1.3f;
        var firstBaselineY = rect.MidY - lineHeight * (lines.Length - 1) / 2f + textPaintTemplate.TextSize * 0.35f;
        for (var i = 0; i < lines.Length; i++)
        {
            canvas.DrawText(lines[i], rect.MidX, firstBaselineY + i * lineHeight, textPaintTemplate);
        }
    }

    private static void DrawConnector(SKCanvas canvas, DiagramShape shape, Func<double, float> toPxX, Func<double, float> toPxY)
    {
        var localPoints = ComputeLocalPath(shape);
        var points = localPoints.Select(p => new SKPoint(toPxX(shape.X + p.X), toPxY(shape.Y + p.Y))).ToArray();

        using var paint = new SKPaint
        {
            Color = shape.LineColorArgb is { } c ? new SKColor(c) : SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)Math.Max(shape.LineWidthEmu / EmuPerPixel, 1),
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
        };

        for (var i = 0; i < points.Length - 1; i++)
        {
            canvas.DrawLine(points[i], points[i + 1], paint);
        }

        if (points.Length < 2)
        {
            return;
        }

        if (shape.HasEndArrow)
        {
            DrawArrowHead(canvas, points[^2], points[^1], paint.Color);
        }

        if (shape.HasStartArrow)
        {
            DrawArrowHead(canvas, points[1], points[0], paint.Color);
        }
    }

    /// <summary>Returns the connector's path in local coordinates (0,0) = shape's own top-left corner,
    /// (Width,Height) = bottom-right - i.e. before translating by <see cref="DiagramShape.X"/>/
    /// <see cref="DiagramShape.Y"/> and before scaling to pixels. See the type doc comment for which
    /// presets get real elbow routing vs. the straight-line fallback.</summary>
    private static (double X, double Y)[] ComputeLocalPath(DiagramShape shape)
    {
        (double X, double Y)[] local = shape.Preset switch
        {
            "bentConnector2" => [(0, 0), (shape.Width, 0), (shape.Width, shape.Height)],
            _ when shape.Preset.StartsWith("bentConnector", StringComparison.Ordinal) =>
                [(0, 0), (shape.Width * shape.BendFraction, 0), (shape.Width * shape.BendFraction, shape.Height), (shape.Width, shape.Height)],
            _ => [(0, 0), (shape.Width, shape.Height)],
        };

        return local.Select(p => (
            shape.FlipHorizontal ? shape.Width - p.X : p.X,
            shape.FlipVertical ? shape.Height - p.Y : p.Y)).ToArray();
    }

    private static void DrawArrowHead(SKCanvas canvas, SKPoint from, SKPoint to, SKColor color)
    {
        const float length = 8f;
        const float angleRad = 25f * MathF.PI / 180f;

        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.5f)
        {
            return;
        }

        var back = new SKPoint(to.X - dx / len * length, to.Y - dy / len * length);
        var wing1 = RotateAround(to, back, angleRad);
        var wing2 = RotateAround(to, back, -angleRad);

        using var fillPaint = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var path = new SKPath();
        path.MoveTo(to);
        path.LineTo(wing1);
        path.LineTo(wing2);
        path.Close();
        canvas.DrawPath(path, fillPaint);
    }

    private static SKPoint RotateAround(SKPoint center, SKPoint point, float angleRad)
    {
        var sin = MathF.Sin(angleRad);
        var cos = MathF.Cos(angleRad);
        var px = point.X - center.X;
        var py = point.Y - center.Y;
        return new SKPoint(px * cos - py * sin + center.X, px * sin + py * cos + center.Y);
    }
}
