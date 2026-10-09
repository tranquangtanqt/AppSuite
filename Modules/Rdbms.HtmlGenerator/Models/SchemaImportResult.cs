using System.Collections.Generic;

namespace Rdbms.HtmlGenerator.Models;

/// <summary>Everything one schema import reads - what every *SchemaImporter returns and
/// RdbmsHtmlGeneratorDatabase.ReplaceAllAsync stores.</summary>
public sealed record SchemaImportResult(
    List<DbTableRecord> Tables,
    List<DbColumnRecord> Columns,
    List<DbForeignKeyRecord> ForeignKeys,
    List<DbIndexRecord> Indexes,
    List<DbConstraintRecord> Constraints);
