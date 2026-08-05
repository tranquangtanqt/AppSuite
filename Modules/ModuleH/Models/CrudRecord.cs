namespace ModuleH.Models;

/// <summary>One imported "CRUD図" logic sheet - unlike ModuleG (1 record per *file*, many sheets each),
/// here 1 record is 1 *sheet* not in the skip list: a workbook can hold dozens/hundreds of these
/// (each sheet = 1 logic/screen ID's CRUD list), see CrudDocImporter.</summary>
public sealed class CrudRecord
{
    /// <summary>The sheet's own name (e.g. "MABSL0140") - also repeated inside the sheet's own header
    /// block under the "文書名"/"CRUD図" label group, 3rd row.</summary>
    public required string ScreenCode { get; init; }

    /// <summary>"ロジック名" - the logic/screen's business display name (e.g. "加工単価マスタ登録").</summary>
    public required string ScreenName { get; init; }

    public required string ModuleId { get; init; }
    public required string ModuleName { get; init; }
    public required string SubModuleId { get; init; }
    public required string SubModuleName { get; init; }
    public required string DocNumber { get; init; }
    public required string Version { get; init; }
    public required string Revision { get; init; }
    public required string SourceFile { get; init; }

    /// <summary>Every non-empty cell's text in the sheet, concatenated - used for the "noi dung"
    /// (content) search box, which doubles as "search by table/object name" (使用オブジェクト) since
    /// those names are part of this text.</summary>
    public required string SearchText { get; init; }

    /// <summary>File name of the exported per-logic HTML (relative to Data\Database\Html\).</summary>
    public required string HtmlFileName { get; init; }

    /// <summary>Number of ID blocks (処理ID) in this sheet - shown as a small badge in the index list.</summary>
    public required int BlockCount { get; init; }
}
