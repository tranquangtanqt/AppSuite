using System.Diagnostics;
using System.Text;
using DbTools.Core;

namespace DbTools.Services;

/// <summary>
/// Tìm và chạy pg_dump / pg_restore / psql của PostgreSQL cài trên máy. Mật khẩu truyền qua biến môi trường PGPASSWORD của
/// tiến trình con (không nằm trên dòng lệnh, không ghi log). Thông báo của công cụ để tiếng Anh (LC_MESSAGES=C) cho khỏi lỗi
/// font / bảng mã khi Windows là tiếng Nhật / Việt.
/// </summary>
public static class PgTools
{
    /// <summary>Thư mục bin có thể có PostgreSQL client: Program Files\PostgreSQL\*\bin (bản cài chuẩn), các thư mục trong PATH.</summary>
    public static IEnumerable<string> CandidateFolders()
    {
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 }.Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var pg = Path.Combine(root, "PostgreSQL");
            if (Directory.Exists(pg))
            {
                foreach (var version in Directory.EnumerateDirectories(pg))
                {
                    yield return Path.Combine(version, "bin");
                }
            }
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return dir.Trim().Trim('"');
        }
    }

    /// <summary>Các bộ client tìm được (mỗi thư mục có pg_dump.exe 1 lần), kèm phiên bản đọc từ `pg_dump --version`.</summary>
    public static async Task<List<PgClient>> FindAsync(IEnumerable<string>? folders = null, CancellationToken ct = default)
    {
        var result = new List<PgClient>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders ?? CandidateFolders())
        {
            string full;
            try
            {
                full = Path.GetFullPath(folder).TrimEnd('\\');
            }
            catch (Exception)
            {
                continue;
            }
            if (!seen.Add(full) || !File.Exists(Path.Combine(full, "pg_dump.exe")))
            {
                continue;
            }
            if (await ProbeAsync(full, ct) is { } client)
            {
                result.Add(client);
            }
        }
        return result.OrderByDescending(c => c.Version).ToList();
    }

    /// <summary>Đọc phiên bản bộ client trong 1 thư mục bin. Không có pg_dump.exe / không chạy được → null.</summary>
    public static async Task<PgClient?> ProbeAsync(string binFolder, CancellationToken ct = default)
    {
        var exe = Path.Combine(binFolder, "pg_dump.exe");
        if (!File.Exists(exe))
        {
            return null;
        }
        var output = new StringBuilder();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var code = await RunAsync(exe, ["--version"], password: null, line => output.AppendLine(line), timeout.Token);
            return code == 0 && PgVersion.Parse(output.ToString()) is { } version ? new PgClient(binFolder, version) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Thư mục bin đã chọn trong cài đặt (rỗng = tự động: bản mới nhất tìm được).</summary>
    public static async Task<PgClient?> ResolveAsync(string configuredFolder, CancellationToken ct = default) =>
        configuredFolder.Trim().Length > 0
            ? await ProbeAsync(configuredFolder.Trim(), ct)
            : PgVersion.Choose(await FindAsync(ct: ct));

    /// <summary>
    /// Chạy 1 công cụ, đẩy từng dòng stdout + stderr ra <paramref name="onLine"/>, trả mã thoát. Huỷ = kết thúc tiến trình
    /// (cả cây) rồi ném OperationCanceledException.
    /// </summary>
    public static async Task<int> RunAsync(string exe, IReadOnlyList<string> args, string? password, Action<string> onLine, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        psi.Environment["LC_MESSAGES"] = "C";
        psi.Environment.Remove("PGPASSWORD");
        if (!string.IsNullOrEmpty(password))
        {
            psi.Environment["PGPASSWORD"] = password;
        }
        using var process = new Process { StartInfo = psi };
        var gate = new object();
        void Line(string? text)
        {
            if (text is not null)
            {
                lock (gate)
                {
                    onLine(text);
                }
            }
        }
        process.OutputDataReceived += (_, e) => Line(e.Data);
        process.ErrorDataReceived += (_, e) => Line(e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // đã thoát
            }
            throw;
        }
        process.WaitForExit(); // chờ đọc hết stdout / stderr
        return process.ExitCode;
    }

    /// <summary>`pg_restore -l` → thông tin file dump; file SQL thường → đọc phần đầu file.</summary>
    public static async Task<DumpArchiveInfo> InspectAsync(PgClient? client, string file, CancellationToken ct)
    {
        var head = new byte[512];
        int read;
        await using (var stream = File.OpenRead(file))
        {
            read = await stream.ReadAsync(head, ct);
        }
        if (!DumpArchiveInfo.IsArchive(head.AsSpan(0, read)))
        {
            var text = new byte[16 * 1024];
            await using var stream = File.OpenRead(file);
            var n = await stream.ReadAsync(text, ct);
            return DumpArchiveInfo.ParsePlainHeader(Encoding.UTF8.GetString(text, 0, n));
        }
        if (client is null)
        {
            throw new InvalidOperationException("Không tìm thấy pg_restore.exe - cài PostgreSQL (bản client) hoặc chọn thư mục bin ở ô PostgreSQL client.");
        }
        var output = new StringBuilder();
        var code = await RunAsync(client.Exe("pg_restore"), ["-l", file], null, l => output.AppendLine(l), ct);
        if (code != 0)
        {
            throw new InvalidOperationException("pg_restore -l lỗi: " + output.ToString().Trim());
        }
        return DumpArchiveInfo.ParseList(output.ToString());
    }
}
