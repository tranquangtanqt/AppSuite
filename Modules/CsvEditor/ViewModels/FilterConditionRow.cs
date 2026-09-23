using CommunityToolkit.Mvvm.ComponentModel;
using CsvEditor.Models;

namespace CsvEditor.ViewModels;

/// <summary>One editable row in the Filter dialog's condition list - UI-facing mutable state only,
/// converted to an immutable <see cref="FilterCondition"/> when the dialog's Apply is confirmed.</summary>
public sealed partial class FilterConditionRow : ObservableObject
{
    [ObservableProperty]
    private int _columnIndex;

    /// <summary>Index into the Operator ComboBox, in the same order as <see cref="FilterOperator"/>'s
    /// declaration - kept as a plain int (rather than binding SelectedIndex to the enum itself) since
    /// x:Bind SelectedIndex needs an int-typed property.</summary>
    [ObservableProperty]
    private int _operatorIndex = (int)FilterOperator.Contains;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private bool _negate;

    public FilterCondition ToCondition() => new()
    {
        ColumnIndex = ColumnIndex,
        Operator = (FilterOperator)OperatorIndex,
        Value = Value,
        Negate = Negate,
    };
}
