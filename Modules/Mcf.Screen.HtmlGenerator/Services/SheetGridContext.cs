using System.Linq;
using OfficeOpenXml;

namespace Mcf.Screen.HtmlGenerator.Services;

/// <summary>Precomputed per-sheet state (merged-cell spans, extracted embedded images) shared between
/// <see cref="ExcelSheetHtmlRenderer.RenderGridRange"/> and the semantic parsers
/// (<see cref="OverviewSheetParser"/>/<see cref="ScreenDiagramSheetParser"/>) so both can render
/// different row ranges of the same sheet without recomputing merges/re-extracting images.</summary>
internal sealed class SheetGridContext
{
    public ExcelWorksheet Sheet { get; }
    public int MinCol { get; }
    public int MaxCol { get; }

    /// <summary>Needed by <see cref="OverviewSheetParser"/> to name/place the 処理関連図/サービス関連図
    /// diagram PNG it rasterizes from the sheet's own shapes (see DiagramRenderer) - same folder/URL
    /// convention as every other embedded image.</summary>
    public string ImagesOutputDir { get; }
    public string ImagesRelativeUrl { get; }

    public Dictionary<(int Row, int Col), (int RowSpan, int ColSpan)> SpanByTopLeft { get; }
    public HashSet<(int Row, int Col)> Covered { get; }

    /// <summary>Relative image URLs anchored at each 1-based (row, col) cell (already written to disk).
    /// An icon anchored inside a merged cell that isn't the merge's top-left is remapped to that
    /// top-left, since that's the only cell of the merge that actually gets a &lt;td&gt; rendered.</summary>
    public Dictionary<(int Row, int Col), List<string>> ImagesByAnchorCell { get; }

    /// <summary>Same images grouped by row only, for callers that don't render a literal cell grid
    /// (semantic section blocks) and just need "roughly this row range".</summary>
    public IEnumerable<(int Row, List<string> Images)> ImagesByAnchorRow =>
        ImagesByAnchorCell
            .GroupBy(kvp => kvp.Key.Row)
            .Select(g => (g.Key, g.SelectMany(kvp => kvp.Value).ToList()));

    public SheetGridContext(ExcelWorksheet sheet, int minCol, int maxCol, string imagesOutputDir, string imagesRelativeUrl)
    {
        Sheet = sheet;
        MinCol = minCol;
        MaxCol = maxCol;
        ImagesOutputDir = imagesOutputDir;
        ImagesRelativeUrl = imagesRelativeUrl;
        (SpanByTopLeft, Covered, var coveredToTopLeft) = ExcelSheetHtmlRenderer.IndexMergedCells(sheet);

        ImagesByAnchorCell = new Dictionary<(int, int), List<string>>();
        foreach (var (anchor, images) in ExcelSheetHtmlRenderer.ExtractImages(sheet, imagesOutputDir, imagesRelativeUrl))
        {
            var target = coveredToTopLeft.TryGetValue(anchor, out var topLeft) ? topLeft : anchor;
            if (!ImagesByAnchorCell.TryGetValue(target, out var list))
            {
                list = new List<string>();
                ImagesByAnchorCell[target] = list;
            }

            list.AddRange(images);
        }
    }
}
