using OfficeOpenXml;

namespace ModuleG.Services;

/// <summary>Precomputed per-sheet state (merged-cell spans, extracted embedded images) shared between
/// <see cref="ExcelSheetHtmlRenderer.RenderGridRange"/> and the semantic parsers
/// (<see cref="OverviewSheetParser"/>/<see cref="ScreenDiagramSheetParser"/>) so both can render
/// different row ranges of the same sheet without recomputing merges/re-extracting images.</summary>
internal sealed class SheetGridContext
{
    public ExcelWorksheet Sheet { get; }
    public int MinCol { get; }
    public int MaxCol { get; }
    public Dictionary<(int Row, int Col), (int RowSpan, int ColSpan)> SpanByTopLeft { get; }
    public HashSet<(int Row, int Col)> Covered { get; }

    /// <summary>Relative image URLs anchored at each 1-based row (already written to disk).</summary>
    public Dictionary<int, List<string>> ImagesByAnchorRow { get; }

    public SheetGridContext(ExcelWorksheet sheet, int minCol, int maxCol, string imagesOutputDir, string imagesRelativeUrl)
    {
        Sheet = sheet;
        MinCol = minCol;
        MaxCol = maxCol;
        (SpanByTopLeft, Covered) = ExcelSheetHtmlRenderer.IndexMergedCells(sheet);
        ImagesByAnchorRow = ExcelSheetHtmlRenderer.ExtractImages(sheet, imagesOutputDir, imagesRelativeUrl);
    }
}
