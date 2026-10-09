using System.Collections.Generic;
using System.Linq;
using Rdbms.HtmlGenerator.Models;

namespace Rdbms.HtmlGenerator.Services;

/// <summary>Every schema importer reads foreign keys as one row per (constraint, column) pair, ordered by
/// table, constraint and column position; this folds them into one DbForeignKeyRecord per constraint with
/// composite keys comma-joined (same shape as Mcf.DbDef.HtmlGenerator).</summary>
internal static class ForeignKeyGrouper
{
    public static List<DbForeignKeyRecord> Group(
        IEnumerable<(string TableName, string ConstraintName, string LocalColumn, string RefTable, string RefColumn)> rows)
    {
        var ordinal = 0;
        return rows
            .GroupBy(r => (r.TableName, r.ConstraintName))
            .Select(group =>
            {
                var groupRows = group.ToList();
                return new DbForeignKeyRecord
                {
                    TableName = group.Key.TableName,
                    OrdinalPosition = ordinal++,
                    LocalColumns = string.Join(",", groupRows.Select(r => r.LocalColumn)),
                    ReferencedTable = groupRows[0].RefTable,
                    ReferencedColumns = string.Join(",", groupRows.Select(r => r.RefColumn)),
                };
            })
            .ToList();
    }
}
