using Microsoft.UI.Xaml.Controls;
using ModuleF.Models;

namespace ModuleF.Views;

/// <summary>Displays an already-computed <see cref="TableStatistics"/> - the async computation (with
/// progress/cancellation) runs before this dialog is constructed, driven from the main window's own
/// status bar/progress UI so Statistics reuses the same busy/cancel affordance as Open.</summary>
public sealed partial class StatisticsDialog : ContentDialog
{
    public StatisticsDialog(TableStatistics statistics)
    {
        InitializeComponent();
        RowCountText.Text = $"Số dòng: {statistics.RowCount:N0}";
        ColumnCountText.Text = $"Số cột: {statistics.ColumnCount:N0}";
        DuplicateCountText.Text = $"Dòng trùng: {statistics.DuplicateRowCount:N0}";
        ColumnsListView.ItemsSource = statistics.PerColumn;
    }
}
