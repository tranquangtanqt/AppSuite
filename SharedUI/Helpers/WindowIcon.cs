using Microsoft.UI.Xaml;

namespace SharedUI.Helpers;

/// <summary>Gán icon của app (Assets\&lt;tên exe&gt;.ico, chép cạnh exe) cho thanh tiêu đề + nút taskbar / Alt+Tab của 1 cửa sổ.
/// App unpackaged không tự lấy icon exe cho cửa sổ WinUI - không gán thì taskbar hiện icon trống. File .ico tạo bằng
/// build\New-AppIcon.ps1; Modules\Directory.Build.props tự dùng nó làm icon exe và chép ra output.</summary>
public static class WindowIcon
{
    /// <summary>Assets\&lt;tên exe&gt;.ico cạnh exe đang chạy (vd CsvEditor.exe → Assets\CsvEditor.ico).</summary>
    public static string DefaultPath { get; } = Path.Combine(AppContext.BaseDirectory, "Assets",
        Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "app") + ".ico");

    /// <summary>Không có file icon thì bỏ qua (cửa sổ giữ icon mặc định), không ném lỗi.</summary>
    public static void Apply(Window window, string? iconPath = null)
    {
        var path = iconPath ?? DefaultPath;
        if (File.Exists(path))
        {
            window.AppWindow.SetIcon(path);
        }
    }
}
