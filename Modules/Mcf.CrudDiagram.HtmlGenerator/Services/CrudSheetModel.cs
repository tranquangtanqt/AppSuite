namespace Mcf.CrudDiagram.HtmlGenerator.Services;

/// <summary>Parsed result of 1 CRUD図 logic sheet - see <see cref="CrudSheetParser"/>.</summary>
internal sealed class CrudSheetModel
{
    public required string ScreenName { get; init; }
    public required string ModuleId { get; init; }
    public required string ModuleName { get; init; }
    public required string SubModuleId { get; init; }
    public required string SubModuleName { get; init; }
    public required string DocNumber { get; init; }
    public required string Version { get; init; }
    public required string Revision { get; init; }

    /// <summary>False when the "使用オブジェクト" data-table header couldn't be found anywhere in the
    /// sheet (unexpected layout) - <see cref="Rows"/> is then empty and the renderer falls back to
    /// dumping the whole sheet as a plain grid instead of silently showing nothing.</summary>
    public required bool DataHeaderFound { get; init; }

    /// <summary>Every row from the data header down to the sheet's last row, 1:1 with the source -
    /// including fully-blank spacer rows between blocks - so the rendered table reproduces the exact
    /// row layout of the original Excel sheet (see <see cref="CrudHtmlRenderer"/>) instead of
    /// regrouping it into separate per-block tables.</summary>
    public required List<CrudTableRow> Rows { get; init; }

    /// <summary>Number of ID blocks (処理ID) in this sheet - a row counts if its <see cref="CrudTableRow.Id"/>
    /// is non-empty - shown as a small badge in the index list.</summary>
    public int BlockCount => Rows.Count(r => !string.IsNullOrWhiteSpace(r.Id));
}

/// <summary>1 row of the CRUD table, verbatim from the source sheet: a block-header row has only
/// <see cref="Id"/>/<see cref="Name"/> filled, an object row has only <see cref="UsedObject"/> and
/// its flags filled, and a spacer row between blocks has everything blank.</summary>
internal sealed class CrudTableRow
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string UsedObject { get; init; }
    public required string Create { get; init; }
    public required string Read { get; init; }
    public required string Update { get; init; }
    public required string Delete { get; init; }
    public required string Kind { get; init; }
    public required string Remark { get; init; }
}
