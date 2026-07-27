using System.Text.Json.Serialization;

namespace ModuleC.Models;

/// <summary>
/// One row of the table/column dictionary loaded from Data\table_columns.json.
/// </summary>
public sealed class TableColumnEntry
{
    [JsonPropertyName("tableName")]
    public string TableName { get; set; } = string.Empty;

    [JsonPropertyName("columnName")]
    public string ColumnName { get; set; } = string.Empty;

    [JsonPropertyName("commentColumn")]
    public string CommentColumn { get; set; } = string.Empty;

    [JsonPropertyName("commentTable")]
    public string CommentTable { get; set; } = string.Empty;
}
