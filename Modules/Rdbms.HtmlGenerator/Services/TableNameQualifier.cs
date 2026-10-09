using System;
using System.Collections.Generic;
using System.Linq;
using Rdbms.HtmlGenerator.Models;

namespace Rdbms.HtmlGenerator.Services;

/// <summary>
/// The SQLite cache and the HTML report key tables by TableName alone, so two schemas each having a table
/// with the same name (vd public.orders + audit.orders) used to fail the import (UNIQUE constraint on
/// Tables.TableName). This renames only the colliding tables to "schema.table" - in the tables, their
/// columns and every foreign key on / pointing to them - and leaves all other names short.
/// A foreign key pointing into a schema that wasn't imported also shows "schema.table", so it isn't
/// mistaken for (and linked to) an imported table of the same name.
/// </summary>
public static class TableNameQualifier
{
    /// <returns>The renamed lists, plus the short names that collided (empty = nothing renamed).</returns>
    public static (List<DbTableRecord> Tables, List<DbColumnRecord> Columns, List<DbForeignKeyRecord> ForeignKeys, List<string> DuplicateNames)
        QualifyDuplicates(List<DbTableRecord> tables, List<DbColumnRecord> columns, List<DbForeignKeyRecord> foreignKeys)
    {
        // Tables' schema is in SourceSheet (that's where every importer puts it).
        var duplicateNames = tables
            .GroupBy(t => t.TableName, StringComparer.Ordinal)
            .Where(g => g.Select(t => t.SourceSheet).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
        var imported = tables.Select(t => (t.SourceSheet, t.TableName)).ToHashSet();

        string Qualify(string schema, string name) =>
            duplicateNames.Contains(name) && schema.Length > 0 ? $"{schema}.{name}" : name;

        string QualifyReference(DbForeignKeyRecord fk)
        {
            if (fk.ReferencedSchema.Length == 0)
            {
                return fk.ReferencedTable;
            }

            return imported.Contains((fk.ReferencedSchema, fk.ReferencedTable))
                ? Qualify(fk.ReferencedSchema, fk.ReferencedTable)
                : fk.ReferencedSchema == fk.Schema ? fk.ReferencedTable : $"{fk.ReferencedSchema}.{fk.ReferencedTable}";
        }

        var newTables = duplicateNames.Count == 0
            ? tables
            : tables.Select(t => t with { TableName = Qualify(t.SourceSheet, t.TableName) }).ToList();
        var newColumns = duplicateNames.Count == 0
            ? columns
            : columns.Select(c => c with { TableName = Qualify(c.Schema, c.TableName) }).ToList();
        var newForeignKeys = foreignKeys
            .Select(f => f with { TableName = Qualify(f.Schema, f.TableName), ReferencedTable = QualifyReference(f) })
            .ToList();

        return (newTables, newColumns, newForeignKeys, duplicateNames.Order(StringComparer.Ordinal).ToList());
    }
}
