using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CsvEditor.Models;
using CsvEditor.ViewModels;

namespace CsvEditor.Views;

public sealed partial class FilterDialog : ContentDialog
{
    public FilterDialog(IReadOnlyList<CsvColumn> columns, FilterExpression? existing)
    {
        InitializeComponent();
        ColumnNames = columns.Select(c => c.Name).ToList();

        if (existing is not null)
        {
            JoinComboBox.SelectedIndex = existing.Join == FilterJoin.Or ? 1 : 0;
            foreach (var condition in existing.Conditions)
            {
                Conditions.Add(new FilterConditionRow
                {
                    ColumnIndex = condition.ColumnIndex,
                    OperatorIndex = (int)condition.Operator,
                    Value = condition.Value,
                    Negate = condition.Negate,
                });
            }
        }

        if (Conditions.Count == 0)
        {
            Conditions.Add(new FilterConditionRow());
        }
    }

    public List<string> ColumnNames { get; }

    public ObservableCollection<FilterConditionRow> Conditions { get; } = new();

    public FilterExpression BuildExpression()
    {
        var expression = new FilterExpression { Join = JoinComboBox.SelectedIndex == 1 ? FilterJoin.Or : FilterJoin.And };
        foreach (var row in Conditions.Where(r => !string.IsNullOrWhiteSpace(r.Value) || r.OperatorIndex is (int)FilterOperator.Regex))
        {
            expression.Conditions.Add(row.ToCondition());
        }

        return expression;
    }

    /// <summary>Sets each row's column ComboBox.ItemsSource directly, rather than
    /// "{Binding ColumnNames, ElementName=Root}" in the DataTemplate (tried and reverted) - an
    /// ElementName binding declared inside a ListView's DataTemplate does not reliably resolve back to
    /// a named element outside the template in WinUI, which left the combobox empty. This fires as each
    /// row's container is (re)used, including on virtualized recycling, so it always has the list by
    /// the time the row's TwoWay ColumnIndex binding tries to apply a selection.</summary>
    private void ConditionsListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer?.ContentTemplateRoot is FrameworkElement root && root.FindName("ColumnComboBox") is ComboBox comboBox)
        {
            comboBox.ItemsSource = ColumnNames;
        }
    }

    private void AddConditionButton_Click(object sender, RoutedEventArgs e) => Conditions.Add(new FilterConditionRow());

    private void RemoveConditionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FilterConditionRow row })
        {
            Conditions.Remove(row);
        }
    }
}
