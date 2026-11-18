using System.Diagnostics;

namespace DbTools.Services;

/// <summary>Phiên bản của thư mục script trong svn / git (ghi vào lịch sử chạy: "DB test đã áp bản nào?").
/// Không có svn.exe / git.exe hoặc thư mục không nằm trong working copy → chuỗi rỗng.</summary>
public static class VcsInfo
{
    public static async Task<string> DescribeAsync(string folder)
    {
        var revision = await RunAsync("svn", $"info --show-item revision --no-newline \"{folder}\"", folder);
        if (revision is { Length: > 0 } && revision.All(char.IsDigit))
        {
            var status = await RunAsync("svn", $"status -q \"{folder}\"", folder);
            return $"svn r{revision}{(string.IsNullOrWhiteSpace(status) ? string.Empty : " (có sửa chưa commit)")}";
        }
        var commit = await RunAsync("git", "rev-parse --short HEAD", folder);
        if (commit is { Length: > 0 } && !commit.Contains(' '))
        {
            var status = await RunAsync("git", "status --porcelain -- .", folder);
            return $"git {commit}{(string.IsNullOrWhiteSpace(status) ? string.Empty : " (có sửa chưa commit)")}";
        }
        return string.Empty;
    }

    private static async Task<string?> RunAsync(string exe, string args, string folder)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                WorkingDirectory = folder,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
            return process.ExitCode == 0 ? (await output).Trim() : null;
        }
        catch (Exception)
        {
            return null; // chưa cài svn / git
        }
    }
}
