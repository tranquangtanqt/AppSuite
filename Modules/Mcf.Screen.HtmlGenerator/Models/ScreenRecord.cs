namespace Mcf.Screen.HtmlGenerator.Models;

/// <summary>One imported "画面説明書" (screen spec) workbook.</summary>
public sealed class ScreenRecord
{
    public required string ScreenCode { get; init; }
    public required string ScreenName { get; init; }
    public required string DocNumber { get; init; }
    public required string Revision { get; init; }
    public required string SourceFile { get; init; }

    /// <summary>Every non-empty cell's text across every sheet, concatenated - used for the "noi
    /// dung" (content) search box.</summary>
    public required string SearchText { get; init; }

    /// <summary>File name of the exported per-screen HTML (relative to Data\Database\Html\).</summary>
    public required string HtmlFileName { get; init; }

    /// <summary>Number of sheets rendered into the page - shown as a small badge in the index list so
    /// a user can tell at a glance how much content a screen has before opening it.</summary>
    public required int SheetCount { get; init; }
}
