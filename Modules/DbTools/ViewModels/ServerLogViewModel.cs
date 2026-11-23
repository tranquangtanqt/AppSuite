using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DbTools.Core;
using DbTools.Models;
using DbTools.Services;
using Microsoft.Extensions.Logging;

namespace DbTools.ViewModels;

/// <summary>1 dòng ở danh sách Nhật ký server (hiển thị).</summary>
public sealed class LogRow(LogEntry entry)
{
    public LogEntry Entry { get; } = entry;
    public LogKind Kind => Entry.Kind;

    /// <summary>"11-23 10:36:44" (bỏ năm, múi giờ cho gọn; chi tiết có đủ).</summary>
    public string Time => Entry.Time.Length >= 19 ? Entry.Time[5..19] : Entry.Time;

    public string KindText => ServerLogParser.KindText(Entry.Kind);
    public string Who => Join(Entry.User, Entry.Database);
    public string Where => Join(Entry.Client, Entry.App == "[unknown]" ? string.Empty : Entry.App);
    public string Ms => Entry.DurationMs?.ToString("#,0.#", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>1 dòng: câu SQL (không có thì thông báo), lỗi thì kèm thông báo lỗi.</summary>
    public string Text
    {
        get
        {
            var text = Entry.Kind == LogKind.Error && Entry.Sql.Length > 0 ? Entry.Message + "  ·  " + Entry.Sql : Entry.Text;
            text = text.ReplaceLineEndings(" ");
            return text.Length > 400 ? text[..400] + "…" : text;
        }
    }

    private static string Join(string a, string b) => a.Length > 0 && b.Length > 0 ? a + " · " + b : a + b;
}

/// <summary>1 ô tích loại dòng (SELECT / INSERT / ... / Server) kèm số dòng của loại đó trong phần đã đọc.</summary>
public sealed partial class LogKindOption(LogKind kind, bool isChecked) : ObservableObject
{
    public LogKind Kind { get; } = kind;

    [ObservableProperty]
    private bool _isChecked = isChecked;

    [ObservableProperty]
    private string _text = ServerLogParser.KindText(kind);
}

/// <summary>1 file log trong ô chọn.</summary>
public sealed class LogFileItem(ServerLogFile file)
{
    public ServerLogFile File { get; } = file;
    public string Display => $"{File.Name}  ·  {LogConfig.Size(File.Size)}  ·  {File.Modified:yyyy-MM-dd HH:mm}{(File.IsCurrent ? "  ·  đang ghi" : string.Empty)}";
}

/// <summary>
/// Trang "Nhật ký server": đọc file log của server PostgreSQL qua SQL (chỉ đọc) - cấu hình log bằng lời, danh sách file, đọc
/// đoạn cuối file, tách thành bảng (giờ, user, database, máy, ứng dụng, loại câu, thời gian chạy, câu SQL), lọc / tìm, xem
/// chi tiết, xuất CSV / TXT. Câu do chính DbTools chạy (application_name AppSuite DbTools) mặc định ẩn.
/// </summary>
public sealed partial class ServerLogViewModel : ObservableObject
{
    public static readonly string[] ReadSizeLabels = ["2 MB cuối", "10 MB cuối", "50 MB cuối", "Cả file"];
    private static readonly long?[] ReadSizes = [2L << 20, 10L << 20, 50L << 20, null];

    private readonly ILogger _log = AppLog.For("ServerLog");
    private List<LogEntry> _entries = [];
    private string _readInfo = string.Empty;

    public ObservableCollection<ConnectionProfile> Profiles => AppState.Profiles;

    public ObservableCollection<LogFileItem> Files { get; } = [];

    public ObservableCollection<LogConfigLine> ConfigLines { get; } = [];

    /// <summary>Mặc định tích hết trừ Kết nối / Server (thường rất nhiều, ít khi cần).</summary>
    public List<LogKindOption> Kinds { get; } = Enum.GetValues<LogKind>()
        .Select(k => new LogKindOption(k, k is not (LogKind.Connection or LogKind.Server))).ToList();

    [ObservableProperty]
    private ConnectionProfile? _profile;

    [ObservableProperty]
    private LogFileItem? _file;

    [ObservableProperty]
    private int _readSizeIndex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(ReadCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "Chọn server (hồ sơ kết nối) rồi bấm Làm mới.";

    [ObservableProperty]
    private string _configSummary = string.Empty;

    [ObservableProperty]
    private bool _configWarning;

    [ObservableProperty]
    private IReadOnlyList<LogRow> _rows = [];

    [ObservableProperty]
    private string _countText = string.Empty;

    /// <summary>Chữ trên nút Loại, vd "Loại: 7/9".</summary>
    [ObservableProperty]
    private string _kindsSummary = string.Empty;

    [ObservableProperty]
    private LogRow? _selected;

    [ObservableProperty]
    private string _detail = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _userText = string.Empty;

    [ObservableProperty]
    private string _databaseText = string.Empty;

    [ObservableProperty]
    private string _clientText = string.Empty;

    [ObservableProperty]
    private bool _hideOwn = true;

    /// <summary>File xuất gần nhất (nút Mở thư mục).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenExportFolderCommand))]
    private string _lastExport = string.Empty;

    public bool IsEmpty => Rows.Count == 0;

    public ServerLogViewModel()
    {
        Profile = Profiles.FirstOrDefault(p => p.Name == AppState.Settings.LastProfile) ?? Profiles.FirstOrDefault();
        foreach (var k in Kinds)
        {
            k.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LogKindOption.IsChecked))
                {
                    ApplyFilter();
                }
            };
        }
        ApplyFilter();
    }

    public void EnsureProfile()
    {
        if (Profile is null || !Profiles.Contains(Profile))
        {
            Profile = Profiles.FirstOrDefault(p => p.Name == AppState.Settings.LastProfile) ?? Profiles.FirstOrDefault();
        }
    }

    partial void OnProfileChanged(ConnectionProfile? value)
    {
        Files.Clear();
        ConfigLines.Clear();
        ConfigSummary = string.Empty;
        _entries = [];
        ApplyFilter();
        Status = "Bấm Làm mới để đọc cấu hình log và danh sách file.";
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnUserTextChanged(string value) => ApplyFilter();

    partial void OnDatabaseTextChanged(string value) => ApplyFilter();

    partial void OnClientTextChanged(string value) => ApplyFilter();

    partial void OnHideOwnChanged(bool value) => ApplyFilter();

    partial void OnRowsChanged(IReadOnlyList<LogRow> value) => OnPropertyChanged(nameof(IsEmpty));

    partial void OnSelectedChanged(LogRow? value) => Detail = value is null ? string.Empty : DetailOf(value.Entry);

    private bool CanWork => !IsBusy;

    /// <summary>Đọc cấu hình log + danh sách file, rồi đọc luôn file server đang ghi.</summary>
    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task RefreshAsync()
    {
        if (Profile is not { } profile)
        {
            Status = "Chưa có kết nối - thêm ở trang Kết nối.";
            return;
        }
        IsBusy = true;
        try
        {
            using var cts = new CancellationTokenSource(Pg.Within(30));
            var info = await PgServerLog.InfoAsync(profile, cts.Token);
            _prefix = new LogLinePrefix(info.Get("log_line_prefix"));
            List<ServerLogFile> files = [];
            string? listError = null;
            try
            {
                files = await PgServerLog.ListFilesAsync(profile, cts.Token);
            }
            catch (Exception ex)
            {
                listError = Pg.Describe(ex);
            }
            ConfigLines.Clear();
            foreach (var line in LogConfig.Describe(info, files.Count, files.Sum(f => f.Size)))
            {
                ConfigLines.Add(line);
            }
            var warnings = ConfigLines.Count(l => l.Warning);
            ConfigWarning = warnings > 0;
            ConfigSummary = $"Cấu hình log: log_statement = {info.Get("log_statement")}, {files.Count} file, {LogConfig.Size(files.Sum(f => f.Size))}"
                + (warnings > 0 ? $" · {warnings} lưu ý" : string.Empty);
            Files.Clear();
            foreach (var f in files)
            {
                Files.Add(new LogFileItem(f));
            }
            if (listError is not null)
            {
                Status = "Không xem được danh sách file log: " + listError;
                return;
            }
            if (Files.Count == 0)
            {
                Status = "Thư mục log của server không có file nào.";
                return;
            }
            File = Files.FirstOrDefault(f => f.File.IsCurrent) ?? Files[0];
            await ReadCoreAsync(profile, File.File);
        }
        catch (Exception ex)
        {
            Status = "Không đọc được cấu hình log: " + Pg.Describe(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private LogLinePrefix? _prefix;

    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task ReadAsync()
    {
        if (Profile is not { } profile || File is not { } file || _prefix is null)
        {
            Status = "Bấm Làm mới trước để lấy danh sách file.";
            return;
        }
        IsBusy = true;
        try
        {
            await ReadCoreAsync(profile, file.File);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReadCoreAsync(ConnectionProfile profile, ServerLogFile file)
    {
        try
        {
            Status = $"Đang đọc {file.Name}...";
            using var cts = new CancellationTokenSource(Pg.Within(5 * 60));
            var chunk = await PgServerLog.ReadAsync(profile, file.Name, ReadSizes[Math.Clamp(ReadSizeIndex, 0, ReadSizes.Length - 1)], cts.Token);
            var result = await Task.Run(() => ServerLogParser.Parse(chunk.Text, _prefix!, chunk.Offset > 0));
            _entries = result.Entries;
            _readInfo = $"{file.Name}: "
                + (chunk.Offset > 0 ? $"đọc {LogConfig.Size(chunk.FileSize - chunk.Offset)} cuối / {LogConfig.Size(chunk.FileSize)}" : $"cả file {LogConfig.Size(chunk.FileSize)}")
                + $", {_entries.Count:N0} mục"
                + (result.UnknownLines > 0 ? $" · {result.UnknownLines:N0} dòng không nhận ra định dạng (khác log_line_prefix?)" : string.Empty)
                + $" · lúc {DateTime.Now:HH:mm:ss}";
            Status = _readInfo;
            _log.LogInformation("Đọc log {File} trên {Host}:{Port}: {Count} mục", file.Name, profile.Host, profile.Port, _entries.Count);
        }
        catch (Exception ex)
        {
            _entries = [];
            Status = $"Không đọc được {file.Name}: {Pg.Describe(ex)}";
        }
        ApplyFilter();
    }

    private LogFilter CurrentFilter() => new(Kinds.Where(k => k.IsChecked).Select(k => k.Kind).ToHashSet(),
        SearchText, UserText, DatabaseText, ClientText, HideOwn);

    private void ApplyFilter()
    {
        var filter = CurrentFilter();
        var shown = _entries.Where(filter.Matches).Select(e => new LogRow(e)).ToList();
        Rows = shown;
        var visible = filter with { Kinds = Enum.GetValues<LogKind>().ToHashSet() };
        foreach (var k in Kinds)
        {
            var count = _entries.Count(e => e.Kind == k.Kind && visible.Matches(e));
            k.Text = $"{ServerLogParser.KindText(k.Kind)} ({count:N0})";
        }
        CountText = _entries.Count == 0 ? string.Empty : $"{shown.Count:N0} / {_entries.Count:N0} mục";
        var on = Kinds.Count(k => k.IsChecked);
        KindsSummary = $"Loại: {(on == Kinds.Count ? "tất cả" : $"{on}/{Kinds.Count}")}{(HideOwn ? string.Empty : " + DbTools")}";
    }

    private static string DetailOf(LogEntry e)
    {
        var head = new List<string> { e.Time };
        if (e.Pid is { } pid)
        {
            head.Add("pid " + pid);
        }
        head.AddRange(new[] { e.User.Length > 0 ? "user " + e.User : "", e.Database.Length > 0 ? "db " + e.Database : "", e.Client, e.App,
            e.Level, e.SqlState }.Where(s => s.Length > 0));
        if (e.DurationMs is { } ms)
        {
            head.Add(ms.ToString("#,0.###", CultureInfo.InvariantCulture) + " ms");
        }
        var sb = new StringBuilder(string.Join("  ·  ", head));
        sb.Append("\r\n\r\n");
        if (e.Sql.Length > 0 && e.Kind == LogKind.Error)
        {
            sb.Append(e.Message.ReplaceLineEndings("\r\n")).Append("\r\n\r\nCâu lỗi:\r\n");
        }
        sb.Append((e.Sql.Length > 0 ? e.Sql : e.Message).ReplaceLineEndings("\r\n"));
        if (e.Detail.Length > 0)
        {
            sb.Append("\r\n\r\n").Append(e.Detail.ReplaceLineEndings("\r\n"));
        }
        return sb.ToString();
    }

    [RelayCommand]
    private void ExportCsv() => Export(".csv", ServerLogExport.ToCsv, new UTF8Encoding(true));

    [RelayCommand]
    private void ExportText() => Export(".txt", ServerLogExport.ToText, new UTF8Encoding(false));

    /// <summary>Ghi các mục đang hiện vào &lt;thư mục dump&gt;\NhatKyServer rồi mở (CSV mở bằng CsvEditor nếu có).</summary>
    private void Export(string extension, Func<IEnumerable<LogEntry>, string> format, Encoding encoding)
    {
        if (Rows.Count == 0)
        {
            Status = "Không có dòng nào để xuất (đọc file / nới bộ lọc trước).";
            return;
        }
        try
        {
            var folder = Path.Combine(AppState.Settings.DumpFolder, "NhatKyServer");
            Directory.CreateDirectory(folder);
            var baseName = string.Concat($"{Profile?.Name}_{Path.GetFileNameWithoutExtension(File?.File.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}"
                .Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch == ' ' ? '_' : ch));
            var path = Path.Combine(folder, baseName + extension);
            System.IO.File.WriteAllText(path, format(Rows.Select(r => r.Entry)), encoding);
            LastExport = path;
            Status = $"Đã xuất {Rows.Count:N0} mục → {path}. Lưu ý: file có nguyên câu SQL (mật khẩu đã che) - đừng gửi ra ngoài nếu có dữ liệu khách.";
            Shell.OpenData(path);
        }
        catch (Exception ex)
        {
            Status = "Không xuất được: " + ex.Message;
        }
    }

    private bool HasExport => LastExport.Length > 0;

    [RelayCommand(CanExecute = nameof(HasExport))]
    private void OpenExportFolder() => Shell.Reveal(LastExport);
}
