using System.Text.RegularExpressions;
using CsvEditor.Models;

namespace CsvEditor.Services;

/// <summary>Compiles a <see cref="FilterExpression"/> into a predicate once per Apply - never
/// re-evaluated per keystroke. View-state only: never mutates <see cref="CsvDocument"/>.</summary>
public sealed class FilterService
{
    public Func<CsvRow, bool> Compile(FilterExpression expression)
    {
        if (expression.Conditions.Count == 0)
        {
            return _ => true;
        }

        var compiled = expression.Conditions.Select(CompileCondition).ToList();
        return expression.Join == FilterJoin.And
            ? row => compiled.All(predicate => predicate(row))
            : row => compiled.Any(predicate => predicate(row));
    }

    private static Func<CsvRow, bool> CompileCondition(FilterCondition condition)
    {
        Func<CsvRow, bool> predicate = condition.Operator switch
        {
            FilterOperator.Contains => row => row.GetCell(condition.ColumnIndex).Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
            FilterOperator.Regex => BuildRegexPredicate(condition),
            _ => BuildComparisonPredicate(condition),
        };

        return condition.Negate ? row => !predicate(row) : predicate;
    }

    private static Func<CsvRow, bool> BuildRegexPredicate(FilterCondition condition)
    {
        var regex = new Regex(condition.Value, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        return row => regex.IsMatch(row.GetCell(condition.ColumnIndex));
    }

    private static Func<CsvRow, bool> BuildComparisonPredicate(FilterCondition condition)
    {
        return row =>
        {
            var cell = row.GetCell(condition.ColumnIndex);
            double cellNumber = 0, valueNumber = 0;
            var bothNumeric = double.TryParse(cell, out cellNumber) && double.TryParse(condition.Value, out valueNumber);

            var comparisonResult = bothNumeric
                ? cellNumber.CompareTo(valueNumber)
                : string.Compare(cell, condition.Value, StringComparison.OrdinalIgnoreCase);

            return condition.Operator switch
            {
                FilterOperator.Equal => bothNumeric ? comparisonResult == 0 : string.Equals(cell, condition.Value, StringComparison.OrdinalIgnoreCase),
                FilterOperator.NotEqual => bothNumeric ? comparisonResult != 0 : !string.Equals(cell, condition.Value, StringComparison.OrdinalIgnoreCase),
                FilterOperator.GreaterThan => comparisonResult > 0,
                FilterOperator.LessThan => comparisonResult < 0,
                FilterOperator.GreaterOrEqual => comparisonResult >= 0,
                FilterOperator.LessOrEqual => comparisonResult <= 0,
                _ => false,
            };
        };
    }
}
