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

    /// <summary>Column bounds before <see cref="ExcelSheetHtmlRenderer.RenderSheet"/> trimmed trailing
    /// visually-empty columns. Floating shapes/connectors (e.g. the 処理関連図/サービス関連図 diagram)
    /// often sit over cells with no text/fill of their own, so the trimmed <see cref="MaxCol"/> can cut
    /// off part of a diagram's width - <see cref="OverviewSheetParser"/>'s diagram-image capture uses
    /// these untrimmed bounds instead, since a plain <c>print area</c> range is authored wide enough to
    /// fit whatever the sheet actually prints, diagram included.</summary>
    public int UntrimmedMinCol { get; }
    public int UntrimmedMaxCol { get; }

    public string ImagesOutputDir { get; }
    public string ImagesRelativeUrl { get; }

    public Dictionary<(int Row, int Col), (int RowSpan, int ColSpan)> SpanByTopLeft { get; }
    public HashSet<(int Row, int Col)> Covered { get; }

    /// <summary>Relative image URLs anchored at each 1-based row (already written to disk).</summary>
    public Dictionary<int, List<string>> ImagesByAnchorRow { get; }

    public SheetGridContext(
        ExcelWorksheet sheet, int minCol, int maxCol, int untrimmedMinCol, int untrimmedMaxCol,
        string imagesOutputDir, string imagesRelativeUrl)
    {
        Sheet = sheet;
        MinCol = minCol;
        MaxCol = maxCol;
        UntrimmedMinCol = untrimmedMinCol;
        UntrimmedMaxCol = untrimmedMaxCol;
        ImagesOutputDir = imagesOutputDir;
        ImagesRelativeUrl = imagesRelativeUrl;
        (SpanByTopLeft, Covered) = ExcelSheetHtmlRenderer.IndexMergedCells(sheet);
        ImagesByAnchorRow = ExcelSheetHtmlRenderer.ExtractImages(sheet, imagesOutputDir, imagesRelativeUrl);
    }
}
