using Common.Logging;
using Microsoft.Extensions.Logging;

namespace ScreenCapture.Services;

/// <summary>Log của module: Logs\screencapture-yyyy-MM-dd.log cạnh exe, qua RollingFileLoggerProvider của
/// Common (cùng định dạng với MainLauncher). Module không có DI container nên dùng 1 điểm truy cập tĩnh.
/// Ghi lỗi bị nuốt (không làm hỏng thao tác của người dùng) và các mốc chính (khởi động, chụp, chụp cuộn,
/// đăng ký phím tắt) để dò các lỗi Win32 khó tái hiện. Chỉ giữ log 14 ngày gần nhất.</summary>
public static class AppLog
{
    private const int KeepDays = 14;
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Logs");
    private static readonly RollingFileLoggerProvider Provider = new(Folder, "screencapture");

    public static ILogger For(string category) => new SafeLogger(Provider.CreateLogger(category));

    /// <summary>Xoá file log cũ hơn <see cref="KeepDays"/> ngày - gọi 1 lần lúc khởi động.</summary>
    public static void CleanupOldFiles()
    {
        try
        {
            if (!Directory.Exists(Folder))
            {
                return;
            }
            foreach (var file in Directory.EnumerateFiles(Folder, "screencapture-*.log"))
            {
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-KeepDays))
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Dọn log không được thì thôi.
        }
    }

    /// <summary>Ghi log không bao giờ được làm lỗi thao tác đang chạy (thư mục chỉ đọc, file bị khoá...).</summary>
    private sealed class SafeLogger(ILogger inner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            try
            {
                inner.Log(logLevel, eventId, state, exception, formatter);
            }
            catch
            {
                // Bỏ qua.
            }
        }
    }
}
