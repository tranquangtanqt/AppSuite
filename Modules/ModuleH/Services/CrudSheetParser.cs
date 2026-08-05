using System.Text;
using OfficeOpenXml;

namespace ModuleH.Services;

/// <summary>
/// The single semantic parser for every CRUD図 logic sheet (unlike ModuleG, which needs 1 parser per
/// distinct sheet layout + a generic-grid fallback) - every sampled sheet across both source
/// workbooks shares the exact same structure:
///
/// - Rows 1-4: a header block where each field's label and value sit in the *same* row (unlike
///   ModuleG's 3-row stacked header) - モジュールID/モジュール名/文書番号/Version/Rev. on row 1,
///   ｻﾌﾞﾓｼﾞｭｰﾙID/ｻﾌﾞﾓｼﾞｭｰﾙ名 on row 2, ロジック名 (this sheet's own business name) on row 3.
/// - 1 data-table header row (seen at row 5 in every sample, but located by matching cell text - not
///   hardcoded - to tolerate minor column drift across files) with columns ID/名称/使用オブジェクト/
///   C/R/U/D/種/備考.
/// - Below that: repeating blocks, each 1 row with a non-empty ID (+ 名称 alongside it) followed by
///   1 row per 使用オブジェクト it touches (+ its C/R/U/D/種/備考 flags), separated from the next
///   block by a fully-blank spacer row. <see cref="CrudSheetParser"/> reads every row verbatim (see
///   <see cref="CrudTableRow"/>) rather than grouping them, so <see cref="CrudHtmlRenderer"/> can
///   reproduce this exact row-by-row layout in 1 continuous table, same as the source Excel sheet.
/// </summary>
internal static class CrudSheetParser
{
    public static CrudSheetModel Parse(ExcelWorksheet sheet, StringBuilder searchText)
    {
        var dim = sheet.Dimension!;

        var moduleId = FindLabelValue(sheet, dim, "モジュールID");
        var moduleName = FindLabelValue(sheet, dim, "モジュール名");
        var subModuleId = FindLabelValue(sheet, dim, "ｻﾌﾞﾓｼﾞｭｰﾙID");
        var subModuleName = FindLabelValue(sheet, dim, "ｻﾌﾞﾓｼﾞｭｰﾙ名");
        var docNumber = FindLabelValue(sheet, dim, "文書番号");
        var version = FindLabelValue(sheet, dim, "Version");
        var revision = FindLabelValue(sheet, dim, "Rev.");
        var screenName = FindLabelValue(sheet, dim, "ロジック名") ?? sheet.Name;

        foreach (var value in new[] { moduleId, moduleName, subModuleId, subModuleName, docNumber, screenName })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                searchText.Append(value).Append(' ');
            }
        }

        var columns = FindDataHeaderColumns(sheet, dim);
        var rows = columns is { } found ? ParseRows(sheet, dim, found, searchText) : new List<CrudTableRow>();

        return new CrudSheetModel
        {
            ScreenName = screenName,
            ModuleId = moduleId ?? string.Empty,
            ModuleName = moduleName ?? string.Empty,
            SubModuleId = subModuleId ?? string.Empty,
            SubModuleName = subModuleName ?? string.Empty,
            DocNumber = docNumber ?? string.Empty,
            Version = version ?? string.Empty,
            Revision = revision ?? string.Empty,
            DataHeaderFound = columns is not null,
            Rows = rows,
        };
    }

    /// <summary>Every label text that can appear in the header block (rows 1-3) - used only to
    /// recognize "the next non-empty cell is actually another label, not this label's value" while
    /// scanning rightward in <see cref="FindLabelValue"/>. The header mixes 2 different micro-layouts
    /// in the same block (confirmed by reading the raw cells, not assumed): モジュールID/モジュール名
    /// /ｻﾌﾞﾓｼﾞｭｰﾙID/ｻﾌﾞﾓｼﾞｭｰﾙ名/ロジック名 sit in the same row as their value, 5 columns to
    /// the right; 文書番号/Version/Rev. instead have their value directly *below* them, same column.</summary>
    private static readonly HashSet<string> HeaderLabels = new()
    {
        "モジュールID", "モジュール名", "ｻﾌﾞﾓｼﾞｭｰﾙID", "ｻﾌﾞﾓｼﾞｭｰﾙ名", "ロジック名", "文書名", "文書番号", "Version", "Rev.",
    };

    /// <summary>Finds a cell in the header block (rows 1-3) whose text exactly matches
    /// <paramref name="label"/>, then returns its value: the nearest non-empty cell to its right on
    /// the same row (stopping - and falling through to the "below" check instead - the moment another
    /// known label is hit, since that means there was no same-row value), or failing that, the cell
    /// directly below it.</summary>
    private static string? FindLabelValue(ExcelWorksheet sheet, ExcelAddressBase dim, string label)
    {
        var lastHeaderRow = Math.Min(dim.End.Row, dim.Start.Row + 3);
        for (var row = dim.Start.Row; row <= lastHeaderRow; row++)
        {
            for (var col = dim.Start.Column; col <= dim.End.Column; col++)
            {
                if (sheet.Cells[row, col].Text != label)
                {
                    continue;
                }

                for (var valueCol = col + 1; valueCol <= dim.End.Column; valueCol++)
                {
                    var text = sheet.Cells[row, valueCol].Text;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    if (HeaderLabels.Contains(text))
                    {
                        break;
                    }

                    return text;
                }

                if (row + 1 <= dim.End.Row)
                {
                    var below = sheet.Cells[row + 1, col].Text;
                    if (!string.IsNullOrWhiteSpace(below) && !HeaderLabels.Contains(below))
                    {
                        return below;
                    }
                }
            }
        }

        return null;
    }

    private readonly record struct DataColumns(int HeaderRow, int Id, int Name, int UsedObject, int C, int R, int U, int D, int Kind, int Remark);

    /// <summary>Scans the first ~15 rows for the row containing both "ID" and "使用オブジェクト" cells,
    /// reading every other column's position from that same row by label text (never a hardcoded
    /// index) so a file with a shifted column still parses correctly.</summary>
    private static DataColumns? FindDataHeaderColumns(ExcelWorksheet sheet, ExcelAddressBase dim)
    {
        var lastRow = Math.Min(dim.End.Row, dim.Start.Row + 14);
        for (var row = dim.Start.Row; row <= lastRow; row++)
        {
            int? id = null, name = null, usedObject = null, c = null, r = null, u = null, d = null, kind = null, remark = null;
            for (var col = dim.Start.Column; col <= dim.End.Column; col++)
            {
                switch (sheet.Cells[row, col].Text)
                {
                    case "ID": id ??= col; break;
                    case "名称": name ??= col; break;
                    case "使用オブジェクト": usedObject ??= col; break;
                    case "C": c ??= col; break;
                    case "R": r ??= col; break;
                    case "U": u ??= col; break;
                    case "D": d ??= col; break;
                    case "種": kind ??= col; break;
                    case "備考": remark ??= col; break;
                }
            }

            if (id is not null && usedObject is not null)
            {
                return new DataColumns(row, id.Value, name ?? 0, usedObject.Value, c ?? 0, r ?? 0, u ?? 0, d ?? 0, kind ?? 0, remark ?? 0);
            }
        }

        return null;
    }

    /// <summary>Reads every row from just below the data header down to the sheet's last row,
    /// 1 <see cref="CrudTableRow"/> per source row - including fully-blank spacer rows between
    /// blocks - so <see cref="CrudHtmlRenderer"/> can reproduce the exact row layout of the original
    /// sheet in a single continuous table instead of regrouping rows into separate per-block tables.</summary>
    private static List<CrudTableRow> ParseRows(ExcelWorksheet sheet, ExcelAddressBase dim, DataColumns cols, StringBuilder searchText)
    {
        var rows = new List<CrudTableRow>();

        for (var row = cols.HeaderRow + 1; row <= dim.End.Row; row++)
        {
            var id = ReadCell(sheet, row, cols.Id);
            var name = ReadCell(sheet, row, cols.Name);
            var usedObject = ReadCell(sheet, row, cols.UsedObject);

            rows.Add(new CrudTableRow
            {
                Id = id,
                Name = name,
                UsedObject = usedObject,
                Create = ReadCell(sheet, row, cols.C),
                Read = ReadCell(sheet, row, cols.R),
                Update = ReadCell(sheet, row, cols.U),
                Delete = ReadCell(sheet, row, cols.D),
                Kind = ReadCell(sheet, row, cols.Kind),
                Remark = ReadCell(sheet, row, cols.Remark),
            });

            foreach (var value in new[] { id, name, usedObject })
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    searchText.Append(value).Append(' ');
                }
            }
        }

        return rows;
    }

    private static string ReadCell(ExcelWorksheet sheet, int row, int col) => col > 0 ? sheet.Cells[row, col].Text : string.Empty;
}
