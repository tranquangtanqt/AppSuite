using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DbTools.Models;

namespace DbTools.Services;

/// <summary>Thư mục dữ liệu của module: Data\ cạnh exe (như các module khác). Biến môi trường DBTOOLS_DATA_DIR đổi sang
/// thư mục khác (test / Sandbox không đụng dữ liệu thật).</summary>
public static class DataPaths
{
    public static string Root { get; set; } =
        Environment.GetEnvironmentVariable("DBTOOLS_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(AppContext.BaseDirectory, "Data");

    public static string Config => Path.Combine(Root, "Config");
    public static string History => Path.Combine(Root, "History");

    /// <summary>File tạm (script xoá sinh ra...).</summary>
    public static string Temp => Path.Combine(Path.GetTempPath(), "AppSuite", "DbTools");
}

/// <summary>Ghi JSON an toàn: ghi file tạm rồi thay, đang ghi mà lỗi thì file cũ còn nguyên.</summary>
internal static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static T Read<T>(string path) where T : new()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T() : new T();
        }
        catch (Exception)
        {
            return new T(); // file hỏng - bắt đầu lại
        }
    }

    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        // Trình diệt virus / tìm kiếm của Windows có thể giữ file vừa ghi trong chốc lát → thay file bị từ chối: thử lại.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                Thread.Sleep(50 * attempt);
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }
}

/// <summary>Hồ sơ kết nối: Data\Config\connections.json. Mật khẩu mã hoá bằng DPAPI (CurrentUser) - file chép sang máy /
/// tài khoản khác thì mật khẩu không giải được (để trống, nhập lại).</summary>
public sealed class ConnectionStore(string? path = null)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AppSuite.DbTools.v1");

    public string FilePath { get; } = path ?? Path.Combine(DataPaths.Config, "connections.json");

    public List<ConnectionProfile> Load()
    {
        var list = JsonFile.Read<List<ConnectionProfile>>(FilePath);
        if (EnsureIds(list))
        {
            // File cũ chưa có mã: ghi lại ngay để lần sau mã không đổi. Ghi thẳng (không qua Save) để giữ nguyên mật khẩu đã mã
            // hoá, kể cả khi không giải được (file của tài khoản / máy khác).
            try
            {
                JsonFile.Write(FilePath, list);
            }
            catch (Exception)
            {
                // Không ghi được (thư mục chỉ đọc...): lần sau cấp lại, chỉ mất "hồ sơ lần trước".
            }
        }
        foreach (var p in list)
        {
            p.Folder = Core.ProfileTree.Normalize(p.Folder);
            p.Password = Unprotect(p.ProtectedPassword);
        }
        return list;
    }

    public void Save(IEnumerable<ConnectionProfile> profiles)
    {
        var list = profiles.ToList();
        EnsureIds(list);
        foreach (var p in list)
        {
            p.ProtectedPassword = Protect(p.Password);
        }
        JsonFile.Write(FilePath, list);
    }

    /// <summary>Cấp mã cho hồ sơ chưa có / trùng mã (file sửa tay). True = có cấp.</summary>
    private static bool EnsureIds(List<ConnectionProfile> list)
    {
        var used = new HashSet<string>();
        var changed = false;
        foreach (var p in list.Where(p => string.IsNullOrEmpty(p.Id) || !used.Add(p.Id)))
        {
            p.Id = Guid.NewGuid().ToString("N");
            changed = true;
        }
        return changed;
    }

    internal static string Protect(string password) => password.Length == 0
        ? string.Empty
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser));

    internal static string Unprotect(string value)
    {
        if (value.Length == 0)
        {
            return string.Empty;
        }
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception)
        {
            return string.Empty; // file của tài khoản / máy khác
        }
    }
}

/// <summary>Cài đặt nhỏ nhớ giữa các lần mở + thứ tự chạy đã lưu theo từng thư mục.</summary>
public sealed class AppSettings
{
    public string LastFolder { get; set; } = string.Empty;
    public bool IncludeSubfolders { get; set; } = true;

    /// <summary>Tên hồ sơ lần trước - chỉ còn để đọc cài đặt cũ, nay nhớ theo <see cref="LastProfileId"/>.</summary>
    public string LastProfile { get; set; } = string.Empty;

    /// <summary>Mã hồ sơ dùng lần trước (<see cref="ConnectionProfile.Id"/>).</summary>
    public string LastProfileId { get; set; } = string.Empty;

    /// <summary>Thư mục đang thu gọn trong cây hồ sơ (trang Kết nối).</summary>
    public List<string> CollapsedFolders { get; set; } = [];

    /// <summary>Thư mục lưu file dump (Dump, Copy DB khác server dùng file tạm ở đây) + bản sao lưu trước khi chạy
    /// (&lt;thư mục&gt;\TruocKhiChay). Đổi ở trang Sao lưu / Khôi phục.</summary>
    public string DumpFolder { get; set; } = DefaultDumpFolder;

    public const string DefaultDumpFolder = @"D:\DMP";

    /// <summary>Thư mục bin của PostgreSQL client (pg_dump / pg_restore / psql). Rỗng = tự động (bản mới nhất tìm được).</summary>
    public string PgBinFolder { get; set; } = string.Empty;

    /// <summary>Chạy (thật) thư mục script: dump trước các bảng / hàm / trigger đang có sẽ bị đổi. Bật sẵn.</summary>
    public bool BackupBeforeRun { get; set; } = true;

    /// <summary>Chờ mở kết nối tối đa bao nhiêu giây (mọi trang, mọi hồ sơ) - server qua VPN chậm thì tăng. Trang Cài đặt.</summary>
    public int ConnectTimeoutSeconds
    {
        get => _connectTimeoutSeconds;
        set => _connectTimeoutSeconds = Math.Clamp(value, MinConnectTimeout, MaxConnectTimeout);
    }

    private int _connectTimeoutSeconds = DefaultConnectTimeout;

    /// <summary>Nhật ký server: thư mục log của server vượt bao nhiêu MB thì cảnh báo (10 - 1.000.000, mặc định 1024).</summary>
    public int LogWarnMb
    {
        get => _logWarnMb;
        set => _logWarnMb = Math.Clamp(value, 10, 1_000_000);
    }

    private int _logWarnMb = 1024;

    /// <summary>Nhật ký server: bảng xếp mới nhất trước (bấm tiêu đề cột Giờ để đổi, nhớ cho lần sau). Mặc định cũ trước (thứ tự file).</summary>
    public bool LogNewestFirst { get; set; }

    /// <summary>Trang Chạy → Chuyển UTF-8: chỗ ghi lần trước (Temp / DumpFolder / Overwrite - tên của DbTools.Core.Utf8Target).</summary>
    public string Utf8Target { get; set; } = "Temp";

    public const int DefaultConnectTimeout = 15;
    public const int MinConnectTimeout = 5;
    public const int MaxConnectTimeout = 120;

    // ----- Sinh dữ liệu mẫu (nhớ lựa chọn lần trước) -----

    /// <summary>Số dòng sinh ra (1 - <see cref="MockMaxRows"/>).</summary>
    public int MockRows { get; set; } = 100;

    public const int MockMaxRows = 1_000_000;

    /// <summary>Số dòng mẫu đọc từ bảng.</summary>
    public int MockSampleRows { get; set; } = 5;
    public bool MockCsv { get; set; } = true;
    public bool MockSql { get; set; } = true;

    /// <summary>Utf8Bom / Utf8 / ShiftJis (tên của DbTools.Core.CsvEncoding).</summary>
    public string MockEncoding { get; set; } = "Utf8Bom";
    public bool MockHeader { get; set; } = true;

    /// <summary>False = NULL ghi ô trống; true = ghi chữ NULL.</summary>
    public bool MockNullAsText { get; set; }

    /// <summary>Thư mục lưu file sinh ra. Rỗng = &lt;thư mục dump&gt;\DuLieuMau.</summary>
    public string MockFolder { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public string MockFolderOrDefault => MockFolder.Trim().Length > 0 ? MockFolder.Trim() : Path.Combine(DumpFolder, "DuLieuMau");

    /// <summary>Thư mục (đường dẫn đầy đủ, viết thường) → danh sách đường dẫn tương đối theo thứ tự chạy đã lưu.</summary>
    public Dictionary<string, List<string>> SavedOrders { get; set; } = [];

    /// <summary>Thư mục → các file đã "Bỏ khỏi danh sách" (đọc lại thư mục vẫn bỏ qua, tới khi Sắp lại tự động).</summary>
    public Dictionary<string, List<string>> ExcludedFiles { get; set; } = [];

    public static string OrderKey(string folder) => Path.GetFullPath(folder).TrimEnd('\\', '/').ToLowerInvariant();
}

public sealed class SettingsStore(string? path = null)
{
    public string FilePath { get; } = path ?? Path.Combine(DataPaths.Config, "settings.json");

    public AppSettings Load() => JsonFile.Read<AppSettings>(FilePath);

    public void Save(AppSettings settings) => JsonFile.Write(FilePath, settings);
}

/// <summary>1 lần chạy (Chạy / Chạy thử) - hiện ở trang Lịch sử chạy.</summary>
public sealed class RunRecord
{
    public DateTime Time { get; set; }
    public string Mode { get; set; } = string.Empty;
    public bool DropFirst { get; set; }
    public string Folder { get; set; } = string.Empty;
    public string Profile { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;

    /// <summary>Schema lúc chạy, vd "schema abc + public" (rỗng ở lịch sử cũ = mặc định).</summary>
    public string Schema { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int FileCount { get; set; }
    public int FilesDone { get; set; }
    public string Error { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public string LogPath { get; set; } = string.Empty;

    /// <summary>Thư mục sao lưu trước khi chạy (rỗng = không sao lưu / không có gì cần sao lưu).</summary>
    public string BackupPath { get; set; } = string.Empty;
}

/// <summary>Lịch sử chạy: Data\History\history.json (mới nhất ở đầu, giữ tối đa <see cref="MaxRecords"/> lần) + log từng lần
/// ở Data\History\Logs.</summary>
public sealed class HistoryStore(string? folder = null)
{
    public const int MaxRecords = 500;

    public string Folder { get; } = folder ?? DataPaths.History;
    public string FilePath => Path.Combine(Folder, "history.json");
    public string LogFolder => Path.Combine(Folder, "Logs");

    public List<RunRecord> Load() => JsonFile.Read<List<RunRecord>>(FilePath);

    public void Add(RunRecord record)
    {
        var list = Load();
        list.Insert(0, record);
        foreach (var old in list.Skip(MaxRecords))
        {
            TryDelete(old.LogPath);
        }
        JsonFile.Write(FilePath, list.Take(MaxRecords).ToList());
    }

    public void Clear()
    {
        foreach (var r in Load())
        {
            TryDelete(r.LogPath);
        }
        JsonFile.Write(FilePath, new List<RunRecord>());
    }

    /// <summary>Đường dẫn file log cho lần chạy bắt đầu lúc <paramref name="time"/>.</summary>
    public string NewLogPath(DateTime time)
    {
        Directory.CreateDirectory(LogFolder);
        var path = Path.Combine(LogFolder, $"run-{time:yyyyMMdd-HHmmss}.log");
        for (var n = 2; File.Exists(path); n++)
        {
            path = Path.Combine(LogFolder, $"run-{time:yyyyMMdd-HHmmss}-{n}.log");
        }
        return path;
    }

    private void TryDelete(string path)
    {
        try
        {
            if (path.Length > 0 && path.StartsWith(Folder, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Không xoá được log cũ thì thôi.
        }
    }
}
