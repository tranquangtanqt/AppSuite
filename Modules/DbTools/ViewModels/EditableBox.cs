using System.Collections.ObjectModel;

namespace DbTools.ViewModels;

/// <summary>ComboBox gõ được (IsEditable) gắn với 1 danh sách + 1 chữ.</summary>
internal static class EditableBox
{
    /// <summary>Xoá danh sách rồi hiện <paramref name="text"/>. Xoá danh sách (kể cả đang rỗng) làm ComboBox hiện trống dù Text còn
    /// nguyên, và đặt lại đúng chữ cũ thì nó không vẽ lại → phải bỏ chữ trước, xoá, rồi mới đặt chữ.</summary>
    public static void Reset(ObservableCollection<string> list, Action<string> setText, string text)
    {
        setText(string.Empty);
        list.Clear();
        setText(text);
    }
}
