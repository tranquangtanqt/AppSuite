using System.Diagnostics;

namespace FileTools.Core;

/// <summary>Tiến độ 1 thao tác: phần đã xong 0–1 + dòng mô tả ngắn (file đang xử lý...).</summary>
public readonly record struct JobProgress(double Fraction, string? Message = null);

/// <summary>Gộp tiến độ: vòng lặp đọc gọi hàng triệu lần/giây, UI chỉ cần ~10 lần/giây.</summary>
public sealed class ProgressThrottle(IProgress<JobProgress>? inner, int intervalMs = 100)
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private long _lastMs = -intervalMs;

    public void Report(double fraction, string? message = null, bool force = false)
    {
        if (inner is null)
        {
            return;
        }
        long now = _watch.ElapsedMilliseconds;
        if (force || now - _lastMs >= intervalMs)
        {
            _lastMs = now;
            inner.Report(new JobProgress(Math.Clamp(fraction, 0, 1), message));
        }
    }
}
