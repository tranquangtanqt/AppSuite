using System.Collections.Generic;
using System.Linq;
using Rdbms.HtmlGenerator.Models;

namespace Rdbms.HtmlGenerator.Services;

/// <summary>One (constraint, column) pair as every schema importer reads foreign keys, ordered by table,
/// constraint and column position.</summary>
internal readonly record struct ForeignKeyColumnRow(
    string Schema, string TableName, string ConstraintName, string LocalColumn,
    string RefSchema, string RefTable, string RefColumn);

/// <summary>Folds <see cref="ForeignKeyColumnRow"/>s into one DbForeignKeyRecord per constraint with composite
/// keys comma-joined (same shape as Mcf.DbDef.HtmlGenerator). Grouped by schema too: two schemas can each
/// have a table and a constraint with the same names.</summary>
internal static class ForeignKeyGrouper
{
    public static List<DbForeignKeyRecord> Group(IEnumerable<ForeignKeyColumnRow> rows)
    {
        var ordinal = 0;
        return rows
            .GroupBy(r => (r.Schema, r.TableName, r.ConstraintName))
            .Select(group =>
            {
                var groupRows = group.ToList();
                return new DbForeignKeyRecord
                {
                    TableName = group.Key.TableName,
                    Schema = group.Key.Schema,
                    OrdinalPosition = ordinal++,
                    LocalColumns = string.Join(",", groupRows.Select(r => r.LocalColumn)),
                    ReferencedTable = groupRows[0].RefTable,
                    ReferencedSchema = groupRows[0].RefSchema,
                    ReferencedColumns = string.Join(",", groupRows.Select(r => r.RefColumn)),
                };
            })
            .ToList();
    }
}
