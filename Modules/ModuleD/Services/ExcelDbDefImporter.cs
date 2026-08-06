using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ModuleD.Models;
using OfficeOpenXml;

namespace ModuleD.Services;

/// <summary>
/// Parses "DBDef" workbooks (Japanese database-definition spreadsheets: an index sheet
/// "テーブル・ビュー一覧" pointing at one or more per-table sheets, each holding one or more table
/// blocks) into <see cref="DbTableRecord"/>/<see cref="DbColumnRecord"/> rows using EPPlus 4.5.3.3.
/// Column positions are located by header text on each sheet rather than hard-coded indexes,
/// because header layout is not perfectly aligned across the 23 source workbooks.
/// </summary>
public sealed class ExcelDbDefImporter
{
    private const string IndexSheetName = "テーブル・ビュー一覧";

    /// <summary>Sheet names known to hold shared "$XXX$" column-group definitions (排他制御用カラム
    /// etc.) - named differently across the 23 workbooks ("制御用" in most, "EXCTRL" in MGDBDef,
    /// "オーダ関連ベース項目" in MSBCDBDef where it holds "$ITM_ORDER_COLS$").</summary>
    private static readonly string[] CommonColumnSheetNames = ["制御用", "EXCTRL", "オーダ関連ベース項目"];

    private static readonly Dictionary<string, List<DbColumnRecord>> EmptyCommonGroups = new(StringComparer.Ordinal);

    private sealed record IndexEntry(string TableName, string JapaneseName, string Kind, string Note);

    private sealed record BlockResult(
        string TableName,
        string Alias,
        string JapaneseName,
        string Description,
        string ManagementType,
        string CautionItems,
        string RevisionHistory,
        List<DbColumnRecord> Columns,
        List<DbForeignKeyRecord> ForeignKeys);

    private sealed record ColumnsResult(List<DbColumnRecord> Columns, List<DbForeignKeyRecord> ForeignKeys);

    private readonly record struct PreambleSections(
        string Description,
        string ManagementType,
        string CautionItems,
        string RevisionHistory);

    /// <summary>Imports every *.xlsm file in <paramref name="directoryPath"/>, de-duplicating table
    /// names across files (first occurrence wins; later duplicates are logged and skipped).</summary>
    public (List<DbTableRecord> Tables, List<DbColumnRecord> Columns, List<DbForeignKeyRecord> ForeignKeys) ImportDirectory(string directoryPath, Action<string> log)
    {
        var tables = new List<DbTableRecord>();
        var columns = new List<DbColumnRecord>();
        var foreignKeys = new List<DbForeignKeyRecord>();
        var seenTableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var files = Directory.Exists(directoryPath)
            ? Directory.GetFiles(directoryPath, "*.xlsm").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
            : [];

        if (files.Count == 0)
        {
            log($"Khong tim thay file .xlsm nao trong {directoryPath}");
            return (tables, columns, foreignKeys);
        }

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            try
            {
                var (fileTables, fileColumns, fileForeignKeys) = ImportFile(file, log);
                foreach (var table in fileTables)
                {
                    if (!seenTableNames.Add(table.TableName))
                    {
                        log($"[{fileName}] Bo qua bang trung ten '{table.TableName}' (da co tu file khac)");
                        continue;
                    }

                    tables.Add(table);
                    columns.AddRange(fileColumns.Where(c => c.TableName == table.TableName));
                    foreignKeys.AddRange(fileForeignKeys.Where(f => f.TableName == table.TableName));
                }

                log($"[{fileName}] Da doc {fileTables.Count} bang.");
            }
            catch (Exception ex)
            {
                log($"[{fileName}] LOI: {ex.Message}");
            }
        }

        return (tables, columns, foreignKeys);
    }

    private (List<DbTableRecord> Tables, List<DbColumnRecord> Columns, List<DbForeignKeyRecord> ForeignKeys) ImportFile(string xlsmPath, Action<string> log)
    {
        var fileName = Path.GetFileName(xlsmPath);
        var tables = new List<DbTableRecord>();
        var columns = new List<DbColumnRecord>();
        var foreignKeys = new List<DbForeignKeyRecord>();

        using var package = new ExcelPackage(new FileInfo(xlsmPath));
        var indexSheet = package.Workbook.Worksheets.FirstOrDefault(s => s.Name.Trim() == IndexSheetName);
        if (indexSheet is null)
        {
            log($"[{fileName}] Khong tim thay sheet '{IndexSheetName}'.");
            return (tables, columns, foreignKeys);
        }

        var entriesBySheet = ParseIndexSheet(indexSheet);
        var commonGroups = LoadCommonColumnGroups(package);
        if (commonGroups.Count > 0)
        {
            log($"[{fileName}] Da nap {commonGroups.Count} nhom cot dung chung (排他制御用カラム...).");
        }

        foreach (var (sheetName, entries) in entriesBySheet)
        {
            var sheet = package.Workbook.Worksheets.FirstOrDefault(s => s.Name.Trim() == sheetName.Trim());
            if (sheet is null)
            {
                log($"[{fileName}] Sheet '{sheetName}' duoc tham chieu tu index nhung khong ton tai.");
                continue;
            }

            var blocks = ParseBlocks(sheet, commonGroups);
            var metaByName = entries.ToDictionary(e => e.TableName, StringComparer.Ordinal);

            foreach (var block in blocks)
            {
                metaByName.TryGetValue(block.TableName, out var meta);
                var japaneseName = block.JapaneseName.Length > 0 ? block.JapaneseName : meta?.JapaneseName ?? string.Empty;

                tables.Add(new DbTableRecord
                {
                    TableName = block.TableName,
                    Alias = block.Alias,
                    JapaneseName = japaneseName,
                    Kind = meta?.Kind ?? string.Empty,
                    Note = meta?.Note ?? string.Empty,
                    Description = block.Description,
                    ManagementType = block.ManagementType,
                    CautionItems = block.CautionItems,
                    RevisionHistory = block.RevisionHistory,
                    SourceFile = fileName,
                    SourceSheet = sheetName,
                });
                columns.AddRange(block.Columns);
                foreignKeys.AddRange(block.ForeignKeys);

                if (block.Columns.Count == 0)
                {
                    log($"[{fileName}] Bang '{block.TableName}' (sheet {sheetName}) khong doc duoc cot nao.");
                }
            }

            var foundNames = blocks.Select(b => b.TableName).ToHashSet(StringComparer.Ordinal);
            foreach (var missing in metaByName.Keys.Except(foundNames))
            {
                log($"[{fileName}] Khong tim thay khoi du lieu cho bang '{missing}' trong sheet '{sheetName}'.");
            }
        }

        return (tables, columns, foreignKeys);
    }

    private static Dictionary<string, List<IndexEntry>> ParseIndexSheet(ExcelWorksheet sheet)
    {
        var result = new Dictionary<string, List<IndexEntry>>();
        var dim = sheet.Dimension;
        if (dim is null)
        {
            return result;
        }

        var grid = ReadGrid(sheet, dim);

        int colTableName = -1, colJapanese = -1, colKind = -1, colNote = -1, colSheetName = -1;

        for (var r = dim.Start.Row; r <= dim.End.Row; r++)
        {
            var headerSheetCol = FindColumn(grid, r, dim, "シート名");
            if (headerSheetCol > 0)
            {
                colTableName = FindColumn(grid, r, dim, "テーブル・ビュー名");
                colJapanese = FindColumn(grid, r, dim, "日本語名");
                colKind = FindColumn(grid, r, dim, "種別");
                colNote = FindColumn(grid, r, dim, "備考");
                colSheetName = headerSheetCol;
                continue;
            }

            if (colSheetName < 0)
            {
                continue;
            }

            var sheetName = grid[r, colSheetName];
            var tableName = colTableName > 0 ? grid[r, colTableName] : string.Empty;
            if (sheetName.Length == 0 || tableName.Length == 0)
            {
                continue;
            }

            var entry = new IndexEntry(
                tableName,
                colJapanese > 0 ? grid[r, colJapanese] : string.Empty,
                colKind > 0 ? grid[r, colKind] : string.Empty,
                colNote > 0 ? grid[r, colNote] : string.Empty);

            if (!result.TryGetValue(sheetName, out var list))
            {
                list = [];
                result[sheetName] = list;
            }

            list.Add(entry);
        }

        return result;
    }

    /// <summary>Parses the "制御用"/"EXCTRL" sheet (if present) into a lookup of "$GROUP_NAME$" ->
    /// its member columns, so table blocks that reference a group by that name (a level-88 row whose
    /// item name is the group name) can have the real columns spliced in instead of being dropped.</summary>
    private static Dictionary<string, List<DbColumnRecord>> LoadCommonColumnGroups(ExcelPackage package)
    {
        var result = new Dictionary<string, List<DbColumnRecord>>(StringComparer.Ordinal);

        foreach (var sheetName in CommonColumnSheetNames)
        {
            var sheet = package.Workbook.Worksheets.FirstOrDefault(s => s.Name.Trim() == sheetName);
            if (sheet is null)
            {
                continue;
            }

            // The group-definition sheet itself never references other groups, so it's parsed with an
            // empty lookup; keepGroupPlaceholders:true keeps the "$..$"-named blocks that ParseBlocks
            // otherwise treats as non-tables and skips - here they ARE the payload we want.
            foreach (var block in ParseBlocks(sheet, EmptyCommonGroups, keepGroupPlaceholders: true))
            {
                if (block.TableName.StartsWith('$'))
                {
                    result[block.TableName] = block.Columns;
                }
            }
        }

        return result;
    }

    private static List<BlockResult> ParseBlocks(
        ExcelWorksheet sheet,
        IReadOnlyDictionary<string, List<DbColumnRecord>> commonGroups,
        bool keepGroupPlaceholders = false)
    {
        var blocks = new List<BlockResult>();
        var dim = sheet.Dimension;
        if (dim is null)
        {
            return blocks;
        }

        var grid = ReadGrid(sheet, dim);

        for (var r = dim.Start.Row; r <= dim.End.Row; r++)
        {
            var colMarkerLabel = GetTableHeaderLabelColumn(grid, r, dim);
            if (colMarkerLabel < 0)
            {
                continue;
            }

            var colAlias = FindColumn(grid, r, dim, "別名");
            var colJapanese = FindColumn(grid, r, dim, "日本語名");
            var colTableName = colMarkerLabel + 1;

            var dataRow = r + 1;
            if (dataRow > dim.End.Row || colTableName > dim.End.Column)
            {
                continue;
            }

            var tableName = grid[dataRow, colTableName];
            if (tableName.Length == 0 || (!keepGroupPlaceholders && tableName.StartsWith('$')))
            {
                // Blank marker row, or (outside the group-definition sheet) a reusable column-group
                // placeholder (e.g. "$EXPNS_COLS$") rather than a real physical table/view.
                continue;
            }

            var alias = colAlias > 0 ? grid[dataRow, colAlias] : string.Empty;
            var japaneseName = colJapanese > 0 ? grid[dataRow, colJapanese] : string.Empty;

            var columnHeaderRow = FindColumnHeaderRow(grid, dim, dataRow + 1);
            var preambleEnd = columnHeaderRow > 0 ? columnHeaderRow - 1 : dim.End.Row;
            var preamble = ParsePreamble(grid, dim, colTableName, dataRow + 1, preambleEnd);
            var columnsResult = columnHeaderRow > 0
                ? ReadColumns(grid, dim, columnHeaderRow, tableName, commonGroups)
                : new ColumnsResult([], []);

            blocks.Add(new BlockResult(
                tableName,
                alias,
                japaneseName,
                preamble.Description,
                preamble.ManagementType,
                preamble.CautionItems,
                preamble.RevisionHistory,
                columnsResult.Columns,
                columnsResult.ForeignKeys));
        }

        return blocks;
    }

    /// <summary>
    /// Scans the free-form area between a table's marker row and its column-definition header for
    /// bracket-labeled sections: "【説明】" (description), "【管理タイプ】" (management type),
    /// "【運用後の変更に注意が必要な項目】" (a "項目名/変更不可理由" mini-table of fields that must
    /// not change after go-live), and "【改廃】" (revision/abolition history). Not every block has
    /// every section, and the ones present can appear in any order.
    /// </summary>
    private static PreambleSections ParsePreamble(string[,] grid, OfficeOpenXml.ExcelAddressBase dim, int labelCol, int fromRow, int toRow)
    {
        var description = string.Empty;
        var managementType = string.Empty;
        var cautionItems = string.Empty;
        var revisionHistory = string.Empty;

        var r = fromRow;
        while (r <= toRow)
        {
            switch (grid[r, labelCol])
            {
                case "【説明】":
                    (description, r) = ReadPlainSection(grid, labelCol, r + 1, toRow);
                    continue;
                case "【管理タイプ】":
                    (managementType, r) = ReadPlainSection(grid, labelCol, r + 1, toRow);
                    continue;
                case "【改廃】":
                    (revisionHistory, r) = ReadPlainSection(grid, labelCol, r + 1, toRow);
                    continue;
                case "【運用後の変更に注意が必要な項目】":
                    (cautionItems, r) = ReadCautionItemsSection(grid, dim, labelCol, r + 1, toRow);
                    continue;
                default:
                    r++;
                    break;
            }
        }

        return new PreambleSections(description, managementType, cautionItems, revisionHistory);
    }

    private static (string Text, int NextRow) ReadPlainSection(string[,] grid, int labelCol, int fromRow, int toRow)
    {
        var lines = new List<string>();
        var r = fromRow;
        for (; r <= toRow; r++)
        {
            var text = grid[r, labelCol];
            if (text.Length == 0 || IsSectionLabel(text))
            {
                break;
            }

            lines.Add(text);
        }

        return (string.Join("\n", lines), r);
    }

    /// <summary>Reads the "項目名 | 変更不可理由" mini-table under "【運用後の変更に注意が必要な項目】"
    /// into "column: reason" lines. Bails out (consuming nothing) if the expected shape isn't found,
    /// so a malformed section can't swallow unrelated rows.</summary>
    private static (string Text, int NextRow) ReadCautionItemsSection(string[,] grid, OfficeOpenXml.ExcelAddressBase dim, int labelCol, int fromRow, int toRow)
    {
        var r = fromRow;
        while (r <= toRow && grid[r, labelCol].Length == 0)
        {
            r++;
        }

        if (r > toRow || grid[r, labelCol] != "項目名")
        {
            return (string.Empty, fromRow);
        }

        var colReason = FindColumn(grid, r, dim, "変更不可理由");
        r++;

        var lines = new List<string>();
        for (; r <= toRow; r++)
        {
            var item = grid[r, labelCol];
            if (item.Length == 0)
            {
                break;
            }

            var reason = colReason > 0 ? grid[r, colReason] : string.Empty;
            lines.Add(reason.Length > 0 ? $"{item}: {reason}" : item);
        }

        return (string.Join("\n", lines), r);
    }

    private static bool IsSectionLabel(string text) => text.Length > 1 && text[0] == '【' && text[^1] == '】';

    private static int FindColumnHeaderRow(string[,] grid, OfficeOpenXml.ExcelAddressBase dim, int fromRow)
    {
        for (var r = fromRow; r <= dim.End.Row; r++)
        {
            if (IsTableHeaderRow(grid, r, dim))
            {
                return -1;
            }

            // Require both "レベル" and "項目名" on the same row: some table blocks embed an
            // unrelated mini-table first (e.g. a "変更不可理由" caution list) whose header row also
            // contains "項目名" alone and would otherwise be mistaken for the real column header.
            if (FindColumn(grid, r, dim, "項目名") > 0 && FindColumn(grid, r, dim, "レベル") > 0)
            {
                return r;
            }
        }

        return -1;
    }

    private static ColumnsResult ReadColumns(
        string[,] grid,
        OfficeOpenXml.ExcelAddressBase dim,
        int headerRow,
        string tableName,
        IReadOnlyDictionary<string, List<DbColumnRecord>> commonGroups)
    {
        var cLevel = FindColumn(grid, headerRow, dim, "レベル");
        var cItem = FindColumn(grid, headerRow, dim, "項目名");
        var cMeta = FindColumn(grid, headerRow, dim, "メタ");
        var cType = FindColumn(grid, headerRow, dim, "型");
        var cLength = FindColumn(grid, headerRow, dim, "桁");
        var cNull = FindColumn(grid, headerRow, dim, "Null");
        var cDefault = FindColumn(grid, headerRow, dim, "Def.");
        var cJapanese = FindColumn(grid, headerRow, dim, "日本語");
        var cDescription = FindColumn(grid, headerRow, dim, "説明");
        var cFullName = FindColumn(grid, headerRow, dim, "フルネーム");
        var cRestriction = FindColumn(grid, headerRow, dim, "値の制限");

        var columns = new List<DbColumnRecord>();
        var foreignKeys = new List<DbForeignKeyRecord>();
        var ordinal = 0;
        var fkOrdinal = 0;

        for (var r = headerRow + 1; r <= dim.End.Row; r++)
        {
            if (IsTableHeaderRow(grid, r, dim))
            {
                break;
            }

            var levelText = cLevel > 0 ? grid[r, cLevel] : string.Empty;
            var itemName = cItem > 0 ? grid[r, cItem] : string.Empty;

            if (levelText == "FOREIGN")
            {
                var fk = ParseForeignKeyRow(grid, dim, r, cLevel);
                if (fk is not null)
                {
                    foreignKeys.Add(new DbForeignKeyRecord
                    {
                        TableName = tableName,
                        OrdinalPosition = fkOrdinal++,
                        LocalColumns = fk.Value.LocalColumns,
                        ReferencedTable = fk.Value.ReferencedTable,
                        ReferencedColumns = fk.Value.ReferencedColumns,
                    });
                }

                continue;
            }

            if (itemName.Length == 0)
            {
                // Blank spacer row - common between/within blocks. Only the next table-header row
                // (checked above) or the end of the sheet actually terminates a block.
                continue;
            }

            if (itemName.StartsWith('$'))
            {
                // A reference to a shared column-group (e.g. "$EXCTRL_COLS$", usually at level 88) -
                // splice in its real columns from the "制御用"/"EXCTRL" sheet instead of dropping it.
                if (commonGroups.TryGetValue(itemName, out var groupColumns))
                {
                    foreach (var groupColumn in groupColumns)
                    {
                        columns.Add(new DbColumnRecord
                        {
                            TableName = tableName,
                            OrdinalPosition = ordinal++,
                            Level = groupColumn.Level,
                            ColumnName = groupColumn.ColumnName,
                            Meta = groupColumn.Meta,
                            DataType = groupColumn.DataType,
                            Length = groupColumn.Length,
                            Nullable = groupColumn.Nullable,
                            DefaultValue = groupColumn.DefaultValue,
                            JapaneseName = groupColumn.JapaneseName,
                            Description = groupColumn.Description,
                            FullName = groupColumn.FullName,
                            ValueRestriction = groupColumn.ValueRestriction,
                            IsCommon = true,
                        });
                    }
                }

                continue;
            }

            if (!int.TryParse(levelText, out var level))
            {
                // Other non-column marker rows (e.g. "INDEX ...", "DYNAMIC_SAMPLING").
                continue;
            }

            columns.Add(new DbColumnRecord
            {
                TableName = tableName,
                OrdinalPosition = ordinal++,
                Level = level,
                ColumnName = itemName,
                Meta = cMeta > 0 ? grid[r, cMeta] : string.Empty,
                DataType = cType > 0 ? grid[r, cType] : string.Empty,
                Length = cLength > 0 ? grid[r, cLength] : string.Empty,
                Nullable = cNull > 0 ? grid[r, cNull] : string.Empty,
                DefaultValue = cDefault > 0 ? grid[r, cDefault] : string.Empty,
                JapaneseName = cJapanese > 0 ? grid[r, cJapanese] : string.Empty,
                Description = cDescription > 0 ? grid[r, cDescription] : string.Empty,
                FullName = cFullName > 0 ? grid[r, cFullName] : string.Empty,
                ValueRestriction = cRestriction > 0 ? grid[r, cRestriction] : string.Empty,
                IsCommon = false,
            });
        }

        return new ColumnsResult(columns, foreignKeys);
    }

    /// <summary>Parses a "FOREIGN | | col[,col...] | | ref_table | | ref_col[,ref_col...]" row: after
    /// the marker column, the local column list, referenced table, and (optional) referenced column
    /// list are simply the next 3 non-blank cells in order - the exact column offsets between those
    /// cells vary slightly across sheets, so this scans for content instead of using fixed indexes.
    /// </summary>
    private static (string LocalColumns, string ReferencedTable, string ReferencedColumns)? ParseForeignKeyRow(
        string[,] grid, OfficeOpenXml.ExcelAddressBase dim, int row, int markerCol)
    {
        var startCol = markerCol > 0 ? markerCol + 1 : dim.Start.Column;
        var tokens = new List<string>();
        for (var c = startCol; c <= dim.End.Column && tokens.Count < 3; c++)
        {
            var text = grid[row, c];
            if (text.Length > 0)
            {
                tokens.Add(text);
            }
        }

        if (tokens.Count < 2)
        {
            return null;
        }

        return (tokens[0], tokens[1], tokens.Count > 2 ? tokens[2] : string.Empty);
    }

    private static bool IsTableHeaderRow(string[,] grid, int row, OfficeOpenXml.ExcelAddressBase dim)
    {
        return GetTableHeaderLabelColumn(grid, row, dim) > 0;
    }

    /// <summary>
    /// Returns the column holding the "テーブル名称"/"テーブル名" block-header label, or -1 if this
    /// row isn't a table-header row. Only the leftmost column of the sheet is checked - not the whole
    /// row - because that exact text can also legitimately appear elsewhere on a data row (e.g. a
    /// column whose own Japanese name happens to be "テーブル名"), which would otherwise be mistaken
    /// for the start of a new block.
    /// </summary>
    private static int GetTableHeaderLabelColumn(string[,] grid, int row, OfficeOpenXml.ExcelAddressBase dim)
    {
        var text = grid[row, dim.Start.Column];
        return text is "テーブル名称" or "テーブル名" ? dim.Start.Column : -1;
    }

    private static int FindColumn(string[,] grid, int row, OfficeOpenXml.ExcelAddressBase dim, string headerText)
    {
        for (var c = dim.Start.Column; c <= dim.End.Column; c++)
        {
            if (grid[row, c] == headerText)
            {
                return c;
            }
        }

        return -1;
    }

    private static string[,] ReadGrid(ExcelWorksheet sheet, OfficeOpenXml.ExcelAddressBase dim)
    {
        var grid = new string[dim.End.Row + 1, dim.End.Column + 1];
        for (var r = dim.Start.Row; r <= dim.End.Row; r++)
        {
            for (var c = dim.Start.Column; c <= dim.End.Column; c++)
            {
                grid[r, c] = sheet.Cells[r, c].Text?.Trim() ?? string.Empty;
            }
        }

        return grid;
    }
}
