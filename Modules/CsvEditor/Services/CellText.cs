namespace CsvEditor.Services;

/// <summary>Xử lý chữ lấy từ ô nhập WinUI: TextBox đổi mọi kiểu xuống dòng thành \r, ghi thẳng vào ô thì không sửa gì cũng
/// làm đổi file và hiện "Chưa lưu".</summary>
public static class CellText
{
    public static bool IsMultiLine(string value) => value.Contains('\n') || value.Contains('\r');

    /// <summary>Đưa xuống dòng trong chữ vừa sửa về kiểu của ô gốc; ô gốc chưa có xuống dòng thì dùng \n (như Excel).</summary>
    public static string RestoreNewlines(string edited, string original)
    {
        if (!IsMultiLine(edited))
        {
            return edited;
        }

        var newline = original.Contains("\r\n") ? "\r\n"
            : original.Contains('\n') ? "\n"
            : original.Contains('\r') ? "\r"
            : "\n";
        return edited.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", newline);
    }
}
