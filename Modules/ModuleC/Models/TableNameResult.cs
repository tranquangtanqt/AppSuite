namespace ModuleC.Models;

/// <summary>One row of the result grid in the "Tim kiem ten bang" dialog.</summary>
public sealed class TableNameResult
{
    public int Stt { get; init; }
    public string TableName { get; init; } = string.Empty;
    public string CommentTable { get; init; } = string.Empty;
}
