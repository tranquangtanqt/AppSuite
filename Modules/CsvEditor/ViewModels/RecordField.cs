using CommunityToolkit.Mvvm.ComponentModel;

namespace CsvEditor.ViewModels;

/// <summary>1 dòng của khung Xem dọc (MainWindow): tên cột + giá trị ô của dòng đang chọn. <see cref="EditText"/> là chữ
/// đang gõ (bind 2 chiều với ô nhập), <see cref="Value"/> là giá trị đã ghi vào bảng - khác nhau nghĩa là còn sửa dở, rời
/// ô thì MainWindow ghi qua <see cref="CsvEditorViewModel.SetCell"/> (có Undo).</summary>
public sealed partial class RecordField : ObservableObject
{
    public RecordField(int columnIndex, string columnName, string value)
    {
        ColumnIndex = columnIndex;
        ColumnName = columnName;
        Value = value;
        _editText = value;
    }

    public int ColumnIndex { get; }

    public string ColumnName { get; }

    /// <summary>Số thứ tự cột (từ 1) + tên - file nhiều cột dễ đối chiếu với tài liệu định nghĩa.</summary>
    public string Label => $"{ColumnIndex + 1}. {ColumnName}";

    public string Value { get; set; }

    [ObservableProperty]
    private string _editText;
}
