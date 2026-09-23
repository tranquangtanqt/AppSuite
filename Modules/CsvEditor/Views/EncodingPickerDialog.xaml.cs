using System.Text;
using Microsoft.UI.Xaml.Controls;

namespace CsvEditor.Views;

public sealed partial class EncodingPickerDialog : ContentDialog
{
    public EncodingPickerDialog()
    {
        InitializeComponent();
    }

    public Encoding SelectedEncoding => ((ComboBoxItem)EncodingComboBox.SelectedItem).Tag switch
    {
        "utf8" => new UTF8Encoding(false),
        "utf8bom" => new UTF8Encoding(true),
        "utf16le" => Encoding.Unicode,
        "utf16be" => Encoding.BigEndianUnicode,
        "shiftjis" => Encoding.GetEncoding(932),
        _ => new UTF8Encoding(false),
    };

    public char SelectedDelimiter => ((string)((ComboBoxItem)DelimiterComboBox.SelectedItem).Tag)[0];
}
