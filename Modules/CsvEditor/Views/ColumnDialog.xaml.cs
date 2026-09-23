using Microsoft.UI.Xaml.Controls;

namespace CsvEditor.Views;

/// <summary>Shared by Add Column and Rename Column - both just collect one column name.</summary>
public sealed partial class ColumnDialog : ContentDialog
{
    public ColumnDialog(string title, string initialName = "")
    {
        InitializeComponent();
        Title = title;
        NameTextBox.Text = initialName;
    }

    public string ColumnName => NameTextBox.Text.Trim();
}
