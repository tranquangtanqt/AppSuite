using System.Text;
using Microsoft.UI.Xaml.Controls;

namespace ModuleF.Views;

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
        _ => new UTF8Encoding(false),
    };

    public char SelectedDelimiter => ((string)((ComboBoxItem)DelimiterComboBox.SelectedItem).Tag)[0];
}
