using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModuleF.Models;
using ModuleF.ViewModels;

namespace ModuleF.Views;

public sealed partial class SortDialog : ContentDialog
{
    public SortDialog(IReadOnlyList<CsvColumn> columns, List<(int ColumnIndex, bool Descending)> existing)
    {
        InitializeComponent();
        ColumnNames = columns.Select(c => c.Name).ToList();

        foreach (var (columnIndex, descending) in existing)
        {
            SortColumns.Add(new SortColumnRow { ColumnIndex = columnIndex, Descending = descending });
        }

        if (SortColumns.Count == 0)
        {
            SortColumns.Add(new SortColumnRow());
        }
    }

    public List<string> ColumnNames { get; }

    public ObservableCollection<SortColumnRow> SortColumns { get; } = new();

    public List<(int ColumnIndex, bool Descending)> BuildSortSpec() =>
        SortColumns.Select(r => (r.ColumnIndex, r.Descending)).ToList();

    private void AddColumnButton_Click(object sender, RoutedEventArgs e) => SortColumns.Add(new SortColumnRow());

    private void RemoveColumnButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SortColumnRow row })
        {
            SortColumns.Remove(row);
        }
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SortColumnRow row })
        {
            var index = SortColumns.IndexOf(row);
            if (index > 0)
            {
                SortColumns.Move(index, index - 1);
            }
        }
    }

    private void MoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SortColumnRow row })
        {
            var index = SortColumns.IndexOf(row);
            if (index >= 0 && index < SortColumns.Count - 1)
            {
                SortColumns.Move(index, index + 1);
            }
        }
    }
}
