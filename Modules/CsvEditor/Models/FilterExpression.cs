namespace CsvEditor.Models;

public enum FilterOperator
{
    Equal,
    NotEqual,
    GreaterThan,
    LessThan,
    GreaterOrEqual,
    LessOrEqual,
    Contains,
    Regex,
}

public enum FilterJoin
{
    And,
    Or,
}

/// <summary>One leaf condition: ColumnIndex <c>Operator</c> Value.</summary>
public sealed class FilterCondition
{
    public required int ColumnIndex { get; init; }
    public required FilterOperator Operator { get; init; }
    public required string Value { get; init; }
    public bool Negate { get; init; }
}

/// <summary>A flat list of conditions joined by a single AND/OR - covers the "AND/OR/NOT of simple
/// conditions" requirement without needing a full expression-tree editor UI. NOT is expressed per
/// condition via <see cref="FilterCondition.Negate"/>.</summary>
public sealed class FilterExpression
{
    public List<FilterCondition> Conditions { get; } = new();
    public FilterJoin Join { get; set; } = FilterJoin.And;
}
