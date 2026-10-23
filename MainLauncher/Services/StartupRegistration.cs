using System;
using System.Linq;
using Microsoft.Win32;

namespace MainLauncher.Services;

/// <summary>Trạng thái "Khởi động cùng Windows" đọc từ registry (không lưu trong launcher-settings.json - registry mới là
/// thứ Windows dùng, người dùng có thể tắt ở Task Manager / Settings > Apps > Startup).</summary>
public enum StartupState
{
    Off,
    On,
    /// <summary>Có mục nhưng trỏ tới MainLauncher.exe ở thư mục khác (vd chép AppSuite sang chỗ mới).</summary>
    OtherPath,
    /// <summary>Có mục nhưng bị tắt ở Task Manager > Startup apps.</summary>
    DisabledByUser,
}

/// <summary>
/// "Khởi động cùng Windows": mục <see cref="ValueName"/> trong HKCU\...\CurrentVersion\Run (chỉ tài khoản Windows hiện tại,
/// không cần quyền admin) = "đường dẫn MainLauncher.exe" --startup. Task Manager tắt mục này bằng cách ghi byte đầu = 3 vào
/// HKCU\...\Explorer\StartupApproved\Run - bật lại từ app thì xoá dấu đó.
/// </summary>
public static class StartupRegistration
{
    public const string ValueName = "AppSuite MainLauncher";

    /// <summary>Tham số khi Windows mở MainLauncher lúc đăng nhập (để mở thu nhỏ nếu người dùng chọn).</summary>
    public const string StartupArgument = "--startup";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public static string ExePath => Environment.ProcessPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "MainLauncher.exe");

    /// <summary>Lần chạy này do Windows mở lúc đăng nhập?</summary>
    public static bool IsStartupLaunch() =>
        Environment.GetCommandLineArgs().Skip(1).Any(a => string.Equals(a, StartupArgument, StringComparison.OrdinalIgnoreCase));

    /// <summary>Trạng thái hiện tại + đường dẫn exe đang đăng ký (null nếu chưa có).</summary>
    public static (StartupState State, string? RegisteredPath) GetState()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        if (run?.GetValue(ValueName) is not string command || command.Length == 0)
        {
            return (StartupState.Off, null);
        }

        var path = ExtractExePath(command);
        if (!string.Equals(Normalize(path), Normalize(ExePath), StringComparison.OrdinalIgnoreCase))
        {
            return (StartupState.OtherPath, path);
        }

        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        // Byte đầu chẵn (2, 6) = bật, lẻ (3) = người dùng đã tắt ở Task Manager.
        if (approved?.GetValue(ValueName) is byte[] { Length: > 0 } flags && (flags[0] & 1) == 1)
        {
            return (StartupState.DisabledByUser, path);
        }
        return (StartupState.On, path);
    }

    /// <summary>Bật cho exe đang chạy (ghi đè mục cũ trỏ chỗ khác, gỡ dấu "đã tắt" của Task Manager).</summary>
    public static void Enable()
    {
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            run.SetValue(ValueName, $"\"{ExePath}\" {StartupArgument}", RegistryValueKind.String);
        }
        DeleteApproval();
    }

    public static void Disable()
    {
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
        {
            run?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        DeleteApproval();
    }

    private static void DeleteApproval()
    {
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>"\"C:\a b\MainLauncher.exe\" --startup" → C:\a b\MainLauncher.exe (không có ngoặc kép thì lấy tới dấu cách đầu).</summary>
    public static string ExtractExePath(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 0 ? command[1..end] : command.Trim('"');
        }
        var space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }

    private static string Normalize(string path)
    {
        try
        {
            return System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        }
        catch (Exception)
        {
            return path;
        }
    }
}
