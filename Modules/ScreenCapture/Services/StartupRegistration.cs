using Microsoft.Win32;

namespace ScreenCapture.Services;

/// <summary>"Khởi động cùng Windows": ghi/xoá 1 giá trị trong HKCU\Software\Microsoft\Windows\
/// CurrentVersion\Run (chỉ cho user hiện tại, không cần quyền admin). Chạy với tham số <c>--tray</c>
/// để app khởi động ngầm ở khay hệ thống, không bật cửa sổ chính lên.</summary>
public static class StartupRegistration
{
    public const string TrayArgument = "--tray";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AppSuite.ScreenCapture";

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } exe)
        {
            key.SetValue(ValueName, $"\"{exe}\" {TrayArgument}");
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName);
        }
    }
}
