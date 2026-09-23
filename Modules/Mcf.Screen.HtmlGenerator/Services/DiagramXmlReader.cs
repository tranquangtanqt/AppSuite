using System.Globalization;
using System.Xml;

namespace Mcf.Screen.HtmlGenerator.Services;

/// <summary>
/// Parses the box/connector shapes belonging to 1 row range straight out of a worksheet's raw
/// DrawingML XML (<c>ExcelDrawings.DrawingXml</c>/<c>NameSpaceManager</c> - EPPlus already parses and
/// exposes these for free, no re-opening the file needed). Bypasses EPPlus's typed
/// <c>ExcelShape</c>/<c>ExcelDrawing</c> API entirely: that API only covers `xdr:sp` box shapes and
/// exposes nothing for `xdr:cxnSp` connectors beyond their bounding box (see DiagramRenderer's doc
/// comment for why that's not enough to draw the 処理関連図/サービス関連図 flow diagram faithfully) -
/// reading straight from XML gets both the box shapes and the connector geometry (straight vs.
/// elbow/`bentConnectorN` + its `adj1` bend guide) from the same, single, consistent coordinate space
/// (`a:off`/`a:ext`, absolute EMU from the sheet's top-left corner), which also sidesteps needing our
/// own row-height/column-width - to - pixel conversion for the row/col-based anchors EPPlus does expose.
/// </summary>
internal static class DiagramXmlReader
{
    /// <summary>Reads every `xdr:twoCellAnchor` whose `xdr:from` row falls within
    /// [<paramref name="fromRow0"/>, <paramref name="toRow0"/>] (both 0-based, matching raw OOXML row
    /// numbering - 1 less than EPPlus's usual 1-based cell rows).</summary>
    public static List<DiagramShape> ReadShapesInRowRange(XmlDocument drawingXml, XmlNamespaceManager ns, int fromRow0, int toRow0)
    {
        var result = new List<DiagramShape>();
        var anchors = drawingXml.SelectNodes("//xdr:twoCellAnchor", ns);
        if (anchors is null)
        {
            return result;
        }

        foreach (XmlNode anchor in anchors)
        {
            var fromRowText = anchor.SelectSingleNode("xdr:from/xdr:row", ns)?.InnerText;
            if (fromRowText is null || !int.TryParse(fromRowText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fromRow))
            {
                continue;
            }

            if (fromRow < fromRow0 || fromRow > toRow0)
            {
                continue;
            }

            var shapeNode = anchor.SelectSingleNode("xdr:sp", ns);
            var isConnector = false;
            if (shapeNode is null)
            {
                shapeNode = anchor.SelectSingleNode("xdr:cxnSp", ns);
                isConnector = true;
            }

            // Anything else here (xdr:pic, xdr:graphicFrame, xdr:grpSp) is out of scope - embedded
            // pictures are already handled by ExcelSheetHtmlRenderer.ExtractImages, and grouped shapes
            // don't appear in these documents.
            if (shapeNode is null)
            {
                continue;
            }

            var shape = TryParseShape(shapeNode, ns, isConnector);
            if (shape is not null)
            {
                result.Add(shape);
            }
        }

        return result;
    }

    private static DiagramShape? TryParseShape(XmlNode shapeNode, XmlNamespaceManager ns, bool isConnector)
    {
        var spPr = shapeNode.SelectSingleNode("xdr:spPr", ns);
        var xfrm = spPr?.SelectSingleNode("a:xfrm", ns);
        var off = xfrm?.SelectSingleNode("a:off", ns);
        var ext = xfrm?.SelectSingleNode("a:ext", ns);
        if (off is null || ext is null)
        {
            return null;
        }

        var x = ParseDouble(off.Attributes?["x"]?.Value);
        var y = ParseDouble(off.Attributes?["y"]?.Value);
        var width = ParseDouble(ext.Attributes?["cx"]?.Value);
        var height = ParseDouble(ext.Attributes?["cy"]?.Value);
        if (x is null || y is null || width is null || height is null)
        {
            return null;
        }

        var prstGeom = spPr!.SelectSingleNode("a:prstGeom", ns);
        var preset = prstGeom?.Attributes?["prst"]?.Value ?? "rect";

        var adj1Fmla = prstGeom?.SelectSingleNode("a:avLst/a:gd[@name='adj1']", ns)?.Attributes?["fmla"]?.Value;
        var bendFraction = TryParseAdjustmentFraction(adj1Fmla) ?? 0.5;

        var line = spPr.SelectSingleNode("a:ln", ns);
        var hasNoLine = line?.SelectSingleNode("a:noFill", ns) is not null;
        var lineWidthEmu = hasNoLine ? 0 : ParseDouble(line?.Attributes?["w"]?.Value) ?? (isConnector ? 9525 : 0);
        var lineColor = GetSolidFillColorArgb(line?.SelectSingleNode("a:solidFill", ns), ns);

        var hasNoFill = spPr.SelectSingleNode("a:noFill", ns) is not null;
        var fillColor = hasNoFill ? null : GetSolidFillColorArgb(spPr.SelectSingleNode("a:solidFill", ns), ns);

        return new DiagramShape
        {
            IsConnector = isConnector,
            Preset = preset,
            X = x.Value,
            Y = y.Value,
            Width = width.Value,
            Height = height.Value,
            FlipHorizontal = xfrm!.Attributes?["flipH"]?.Value == "1",
            FlipVertical = xfrm.Attributes?["flipV"]?.Value == "1",
            BendFraction = bendFraction,
            Text = isConnector ? null : ReadText(shapeNode, ns),
            FillColorArgb = fillColor,
            LineColorArgb = lineColor ?? (isConnector ? 0xFF000000 : null),
            LineWidthEmu = lineWidthEmu,
            HasStartArrow = HasArrow(line, "a:headEnd", ns),
            HasEndArrow = HasArrow(line, "a:tailEnd", ns),
        };
    }

    private static bool HasArrow(XmlNode? line, string elementName, XmlNamespaceManager ns)
    {
        var type = line?.SelectSingleNode(elementName, ns)?.Attributes?["type"]?.Value;
        return type is not (null or "none");
    }

    /// <summary>DrawingML has 5 ways to name a color; these documents only ever use an explicit RGB
    /// (`a:srgbClr val="RRGGBB"`) or a Windows system color (`a:sysClr val="window" lastClr="RRGGBB"` -
    /// the `lastClr` attribute is Excel's own cached RGB snapshot of whatever that system color
    /// resolved to when saved, which is exactly the approximation needed here). Theme colors
    /// (`a:schemeClr`) don't appear in any sample seen and aren't resolved - a shape using one renders
    /// unfilled/black rather than guessing a theme's accent color.</summary>
    private static uint? GetSolidFillColorArgb(XmlNode? solidFill, XmlNamespaceManager ns)
    {
        if (solidFill is null)
        {
            return null;
        }

        var rgb = solidFill.SelectSingleNode("a:srgbClr", ns)?.Attributes?["val"]?.Value
            ?? solidFill.SelectSingleNode("a:sysClr", ns)?.Attributes?["lastClr"]?.Value;
        return ParseRgbHex(rgb);
    }

    /// <summary>Joins every text run across every paragraph in the shape's text body - paragraphs
    /// (`a:p`) and manual line breaks (`a:br`) both become a newline, runs (`a:r/a:t`) between them
    /// concatenated directly, matching how EPPlus's own <c>ExcelShape.Text</c> flattens multi-run
    /// labels (e.g. "MSBBL6020" + `a:br` + "受注登録サービス" -> 2 lines, not 1 run-on line).</summary>
    private static string? ReadText(XmlNode shapeNode, XmlNamespaceManager ns)
    {
        var paragraphs = shapeNode.SelectNodes("xdr:txBody/a:p", ns);
        if (paragraphs is null || paragraphs.Count == 0)
        {
            return null;
        }

        var lines = new List<string>();
        foreach (XmlNode p in paragraphs)
        {
            var current = new System.Text.StringBuilder();
            foreach (XmlNode child in p.ChildNodes)
            {
                switch (child.LocalName)
                {
                    case "r":
                        current.Append(child.SelectSingleNode("a:t", ns)?.InnerText);
                        break;
                    case "br":
                        lines.Add(current.ToString());
                        current.Clear();
                        break;
                }
            }

            lines.Add(current.ToString());
        }

        var joined = string.Join("\n", lines).Trim();
        return joined.Length > 0 ? joined : null;
    }

    /// <summary>DrawingML adjustment guides are encoded as e.g. <c>fmla="val 50000"</c>, a 0-100000
    /// scale (100000 = 100%).</summary>
    private static double? TryParseAdjustmentFraction(string? fmla)
    {
        if (fmla is null)
        {
            return null;
        }

        var parts = fmla.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[0] != "val")
        {
            return null;
        }

        return ParseDouble(parts[1]) is { } value ? value / 100000.0 : null;
    }

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static uint? ParseRgbHex(string? hex) =>
        hex is not null && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? 0xFF000000 | rgb
            : null;
}
