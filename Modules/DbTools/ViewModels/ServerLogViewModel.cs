using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DbTools.Core;
using DbTools.Models;
using DbTools.Services;
using Microsoft.Extensions.Logging;

namespace DbTools.ViewModels;

/// <summary>1 dòng ở danh sách Nhật ký server (hiển thị). <see cref="Index"/> = vị trí mục trong phần đã đọc.</summary>
public sealed class LogRow(LogEntry entry, int index)
{
    public LogEntry Entry { get; } = entry;
    public int Index { get; } = index;
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

/// <summary>ObservableCollection thay cả danh sách bằng 1 sự kiện Reset (lọc lại vài chục nghìn dòng không phát từng dòng).</summary>
public sealed class LogRowCollection : ObservableCollection<LogRow>
{
    public void ReplaceAll(IEnumerable<LogRow> rows)
    {
        Items.Clear();
        foreach (var row in rows)
        {
            Items.Add(row);
        }
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
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
/// Trang "Nhật ký server": đọc file log của server PostgreSQL qua SQL (chỉ đọc) - cấu hình log bằng lời (kèm dung lượng,
/// tốc độ tăng, cảnh báo ngưỡng), danh sách file, đọc đoạn cuối file, tách thành bảng (giờ, user, database, máy, ứng dụng,
/// loại câu, thời gian chạy, câu SQL), lọc / tìm, chi tiết, xuất CSV / TXT; Theo dõi = mỗi ~2 giây đọc tiếp phần mới của file
/// server đang ghi (tự sang file mới khi server xoay vòng). Câu do chính DbTools chạy mặc định ẩn.
/// </summary>
public sealed partial class ServerLogViewModel : ObservableObject
{
    public static readonly string[] ReadSizeLabels = ["2 MB cuối", "10 MB cuối", "50 MB cuối", "Cả file"];
    private static readonly long?[] ReadSizes = [2L << 20, 10L << 20, 50L << 20, null];

    /// <summary>Mỗi lần Theo dõi đọc tối đa bấy nhiêu byte mới (ghi nhanh hơn thì bỏ phần giữa, đọc phần cuối).</summary>
    private const long FollowChunk = 5L << 20;

    public static readonly TimeSpan FollowInterval = TimeSpan.FromSeconds(2);

    private readonly ILogger _log = AppLog.For("ServerLog");
    private ServerLogReader? _reader;
    private List<LogEntry> Entries => _reader?.Entries ?? [];
    private LogLinePrefix? _prefix;

    /// <summary>File đang hiện + đọc tới byte nào (Theo dõi đọc tiếp từ đây).</summary>
    private string _readFile = string.Empty;
    private long _offset;

    private CancellationTokenSource? _followCts;
    private bool _switchingFile;

    public ObservableCollection<ConnectionProfile> Profiles => AppState.Profiles;

    public ObservableCollection<LogFileItem> Files { get; } = [];

    public ObservableCollection<LogConfigLine> ConfigLines { get; } = [];

    public LogRowCollection Rows { get; } = [];

    /// <summary>Mặc định tích hết trừ Kết nối / Server (thường rất nhiều, ít khi cần).</summary>
    public List<LogKindOption> Kinds { get; } = Enum.GetValues<LogKind>()
        .Select(k => new LogKindOption(k, k is not (LogKind.Connection or LogKind.Server))).ToList();

    /// <summary>Theo dõi sắp / vừa thêm dòng cuối bảng - trang tự cuộn xuống nếu đang ở cuối.</summary>
    public event EventHandler? RowsAppending;
    public event EventHandler? RowsAppended;

    /// <summary>Bắt đầu Theo dõi / đổi bộ lọc lúc đang theo dõi: trang nhảy xuống dòng cuối (từ đó dòng mới tự cuộn theo).</summary>
    public event EventHandler? ScrollToEndRequested;

    [ObservableProperty]
    private ConnectionProfile? _profile;

    [ObservableProperty]
    private LogFileItem? _file;

    [ObservableProperty]
    private int _readSizeIndex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(ReadCommand), nameof(FollowCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(ReadCommand))]
    [NotifyPropertyChangedFor(nameof(FollowText))]
    private bool _isFollowing;

    public string FollowText => IsFollowing ? "Dừng theo dõi" : "Theo dõi";

    [ObservableProperty]
    private string _status = "Chọn server (hồ sơ kết nối) rồi bấm Làm mới.";

    [ObservableProperty]
    private string _configSummary = string.Empty;

    [ObservableProperty]
    private bool _configWarning;

    [ObservableProperty]
    private string _countText = string.Empty;

    /// <summary>Chữ trên nút Loại, vd "Loại: 7/9".</summary>
    [ObservableProperty]
    private string _kindsSummary = string.Empty;

    [ObservableProperty]
    private LogRow? _selected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopySqlCommand), nameof(CopySqlInlinedCommand))]
    private string _detail = string.Empty;

    /// <summary>Mục đang hiện ở ô chi tiết (giữ cả khi lọc làm mất dòng đang chọn).</summary>
    private LogEntry? _detailEntry;

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

    /// <summary>Mới nhất trước (đảo thứ tự file). Nhớ trong settings.json.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeHeader), nameof(TimeHeaderTip))]
    private bool _newestFirst = AppState.Settings.LogNewestFirst;

    public string TimeHeader => NewestFirst ? "Giờ ↓" : "Giờ ↑";

    public string TimeHeaderTip => NewestFirst
        ? "Đang xếp mới nhất trước - bấm để xếp cũ nhất trước"
        : "Đang xếp cũ nhất trước (thứ tự trong file) - bấm để xếp mới nhất trước";

    partial void OnNewestFirstChanged(bool value)
    {
        AppState.Settings.LogNewestFirst = value;
        AppState.SaveSettings();
        ApplyFilter();
        ScrollToEndRequested?.Invoke(this, EventArgs.Empty); // về phía dòng mới nhất
    }

    [RelayCommand]
    private void ToggleSort() => NewestFirst = !NewestFirst;

    /// <summary>File xuất gần nhất (nút Mở thư mục).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenExportFolderCommand))]
    private string _lastExport = string.Empty;

    public bool IsEmpty => Rows.Count == 0;

    public ServerLogViewModel()
    {
        Profile = AppState.LastProfile;
        Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
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
            Profile = AppState.LastProfile;
        }
    }

    partial void OnProfileChanged(ConnectionProfile? value)
    {
        StopFollow();
        Files.Clear();
        ConfigLines.Clear();
        ConfigSummary = string.Empty;
        _reader = null;
        _readFile = string.Empty;
        _detailEntry = null;
        Detail = string.Empty;
        ApplyFilter();
        Status = "Bấm Làm mới để đọc cấu hình log và danh sách file.";
    }

    partial void OnFileChanged(LogFileItem? value)
    {
        if (IsFollowing && !_switchingFile && value?.File.Name != _readFile)
        {
            StopFollow(); // chọn file khác = thôi theo dõi file đang ghi
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnUserTextChanged(string value) => ApplyFilter();

    partial void OnDatabaseTextChanged(string value) => ApplyFilter();

    partial void OnClientTextChanged(string value) => ApplyFilter();

    partial void OnHideOwnChanged(bool value) => ApplyFilter();

    partial void OnSelectedChanged(LogRow? value)
    {
        if (value is not null)
        {
            _detailEntry = value.Entry;
            Detail = DetailOf(value.Entry);
            CopySqlCommand.NotifyCanExecuteChanged();
            CopySqlInlinedCommand.NotifyCanExecuteChanged();
        }
    }

    private bool HasSql => _detailEntry is { Sql.Length: > 0 } && Detail.Length > 0;

    private bool HasParameters => HasSql && SqlParameters.Parse(_detailEntry!.Detail).Count > 0;

    /// <summary>Chép đúng câu SQL của dòng đang xem (không kèm giờ / DETAIL).</summary>
    [RelayCommand(CanExecute = nameof(HasSql))]
    private void CopySql()
    {
        SetClipboard(_detailEntry!.Sql);
        Status = "Đã chép câu SQL.";
    }

    /// <summary>Chép câu SQL đã thay $1, $2... bằng giá trị ở dòng "parameters:" của log, dòng có thay kèm chú thích "-- $1 = ...".</summary>
    [RelayCommand(CanExecute = nameof(HasParameters))]
    private void CopySqlInlined()
    {
        var e = _detailEntry!;
        var result = SqlParameters.Inline(e.Sql, SqlParameters.Parse(e.Detail));
        SetClipboard(result.Sql);
        Status = $"Đã chép câu SQL, thay {result.Replaced} chỗ tham số"
            + (result.Missing.Count > 0 ? $"; log không ghi giá trị của {string.Join(", ", result.Missing.Select(n => "$" + n))} - giữ nguyên." : ".");
    }

    private static void SetClipboard(string text)
    {
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(text.ReplaceLineEndings("\r\n"));
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

    private bool CanWork => !IsBusy && !IsFollowing;

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
            if (!await LoadInfoAsync(profile) || File is null)
            {
                return;
            }
            await ReadCoreAsync(profile, File.File);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Cấu hình log + danh sách file; chọn sẵn file đang ghi. False = không lấy được danh sách file.</summary>
    private async Task<bool> LoadInfoAsync(ConnectionProfile profile)
    {
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
            var stats = files.Select(f => new LogFileStat(f.Name, f.Size, f.Modified)).ToList();
            foreach (var line in LogConfig.Describe(info, stats, AppState.Settings.LogWarnMb * (1L << 20), DateTime.Now))
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
                return false;
            }
            if (Files.Count == 0)
            {
                Status = "Thư mục log của server không có file nào.";
                return false;
            }
            File = Files.FirstOrDefault(f => f.File.IsCurrent) ?? Files[0];
            return true;
        }
        catch (Exception ex)
        {
            Status = "Không đọc được cấu hình log: " + Pg.Describe(ex);
            return false;
        }
    }

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

    private async Task<bool> ReadCoreAsync(ConnectionProfile profile, ServerLogFile file)
    {
        try
        {
            Status = $"Đang đọc {file.Name}...";
            using var cts = new CancellationTokenSource(Pg.Within(5 * 60));
            var chunk = await PgServerLog.ReadAsync(profile, file.Name, ReadSizes[Math.Clamp(ReadSizeIndex, 0, ReadSizes.Length - 1)], cts.Token);
            var reader = new ServerLogReader(_prefix!);
            await Task.Run(() => reader.Append(chunk.Text, chunk.Offset > 0)); // giữ dòng dở cuối file cho Theo dõi
            _reader = reader;
            _readFile = file.Name;
            _offset = chunk.FileSize;
            Status = $"{file.Name}: "
                + (chunk.Offset > 0 ? $"đọc {LogConfig.Size(chunk.FileSize - chunk.Offset)} cuối / {LogConfig.Size(chunk.FileSize)}" : $"cả file {LogConfig.Size(chunk.FileSize)}")
                + $", {Entries.Count:N0} mục"
                + (reader.UnknownLines > 0 ? $" · {reader.UnknownLines:N0} dòng không nhận ra định dạng (khác log_line_prefix?)" : string.Empty)
                + $" · lúc {DateTime.Now:HH:mm:ss}";
            _log.LogInformation("Đọc log {File} trên {Host}:{Port}: {Count} mục", file.Name, profile.Host, profile.Port, Entries.Count);
            ApplyFilter();
            return true;
        }
        catch (Exception ex)
        {
            _reader = null;
            Status = $"Không đọc được {file.Name}: {Pg.Describe(ex)}";
            ApplyFilter();
            return false;
        }
    }

    private bool CanFollow => !IsBusy;

    /// <summary>Theo dõi / Dừng theo dõi. Bắt đầu: đọc file server đang ghi (nếu đang xem file khác) rồi mỗi ~2 giây đọc tiếp.</summary>
    [RelayCommand(CanExecute = nameof(CanFollow))]
    private async Task FollowAsync()
    {
        if (IsFollowing)
        {
            StopFollow();
            return;
        }
        if (Profile is not { } profile)
        {
            Status = "Chưa có kết nối - thêm ở trang Kết nối.";
            return;
        }
        IsBusy = true;
        try
        {
            if (_prefix is null || Files.Count == 0)
            {
                if (!await LoadInfoAsync(profile))
                {
                    return;
                }
            }
            var current = Files.FirstOrDefault(f => f.File.IsCurrent);
            if (current is not null && (current.File.Name != _readFile || _reader is null))
            {
                File = current;
                if (!await ReadCoreAsync(profile, current.File))
                {
                    return;
                }
            }
            else if (_reader is null && File is not null && !await ReadCoreAsync(profile, File.File))
            {
                return;
            }
        }
        finally
        {
            IsBusy = false;
        }
        var cts = new CancellationTokenSource();
        _followCts = cts;
        IsFollowing = true;
        ScrollToEndRequested?.Invoke(this, EventArgs.Empty);
        Status = $"Đang theo dõi {_readFile} (mỗi {FollowInterval.TotalSeconds:0} giây đọc phần mới)...";
        _log.LogInformation("Theo dõi log {File} trên {Host}:{Port}", _readFile, profile.Host, profile.Port);
        _ = FollowLoopAsync(profile, cts.Token); // chạy nền: lệnh xong ngay, nút "Dừng theo dõi" bấm được
    }

    /// <summary>Thôi theo dõi (nút Dừng, rời trang, đổi server / file).</summary>
    public void StopFollow()
    {
        if (_followCts is null)
        {
            return;
        }
        _followCts.Cancel();
        _followCts = null;
        IsFollowing = false;
        Status = $"Đã dừng theo dõi {_readFile} · {Entries.Count:N0} mục · lúc {DateTime.Now:HH:mm:ss}";
    }

    private async Task FollowLoopAsync(ConnectionProfile profile, CancellationToken ct)
    {
        ServerLogFollower? follower = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(FollowInterval, ct);
                try
                {
                    follower ??= await ServerLogFollower.OpenAsync(profile, ct);
                    var added = await PollAsync(follower, ct);
                    if (!ct.IsCancellationRequested)
                    {
                        Status = $"Đang theo dõi {_readFile} · {Entries.Count:N0} mục"
                            + (added > 0 ? $" · +{added} mục mới" : string.Empty) + $" · lúc {DateTime.Now:HH:mm:ss}";
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    // Mất kết nối / server khởi động lại: báo, lần sau mở kết nối mới rồi thử tiếp.
                    Status = $"Theo dõi: lỗi đọc ({Pg.Describe(ex)}) - thử lại sau {FollowInterval.TotalSeconds:0} giây...";
                    if (follower is not null)
                    {
                        await follower.DisposeAsync();
                        follower = null;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (follower is not null)
            {
                await follower.DisposeAsync();
            }
        }
    }

    /// <summary>1 lượt: đọc phần mới của file đang theo dõi; file bị ghi đè (ngắn lại) thì đọc lại từ đầu; server đã chuyển
    /// sang file khác thì đọc nốt file cũ rồi sang file mới. Trả số mục mới.</summary>
    private async Task<int> PollAsync(ServerLogFollower follower, CancellationToken ct)
    {
        var before = Entries.Count;
        var (current, size) = await follower.StatAsync(_readFile, ct);
        if (size is { } s && s < _offset)
        {
            _offset = 0; // xoay vòng ghi đè cùng tên (log_truncate_on_rotation)
        }
        if (size is { } length && length > _offset)
        {
            await ReadNewAsync(follower, _readFile, length, ct);
        }
        if (current.Length > 0 && current != _readFile)
        {
            _readFile = current;
            _offset = 0;
            SelectFile(current);
            var (_, newSize) = await follower.StatAsync(current, ct);
            if (newSize is { } n && n > 0)
            {
                await ReadNewAsync(follower, current, n, ct);
            }
        }
        return Entries.Count - before;
    }

    private async Task ReadNewAsync(ServerLogFollower follower, string file, long size, CancellationToken ct)
    {
        var start = Math.Max(_offset, size - FollowChunk);
        var text = await follower.ReadAsync(file, start, size - start, ct);
        ct.ThrowIfCancellationRequested();
        if (start > _offset)
        {
            // Ghi nhanh hơn 5 MB / lượt: bỏ phần giữa - kết thúc dòng dở trước đó, bỏ dòng đầu bị cắt của đoạn mới.
            _reader!.Append("\n");
            var newline = text.IndexOf('\n');
            text = newline >= 0 ? text[(newline + 1)..] : string.Empty;
        }
        _offset = size;
        AppendRows(_reader!.Append(text));
    }

    /// <summary>Server sang file mới: chọn file đó trong ô (thêm vào nếu danh sách chưa có), không dừng theo dõi.</summary>
    private void SelectFile(string name)
    {
        _switchingFile = true;
        try
        {
            var item = Files.FirstOrDefault(f => f.File.Name == name);
            if (item is null)
            {
                item = new LogFileItem(new ServerLogFile(name, 0, DateTime.Now, true));
                Files.Insert(0, item);
            }
            File = item;
        }
        finally
        {
            _switchingFile = false;
        }
    }

    /// <summary>Theo dõi: cập nhật dòng cuối bảng - bỏ các dòng của mục từ <paramref name="from"/> (mục cuối có thể vừa
    /// nhận thêm dòng nối tiếp), thêm lại các mục từ đó trở đi nếu khớp bộ lọc.</summary>
    private void AppendRows(int from)
    {
        var filter = CurrentFilter();
        RowsAppending?.Invoke(this, EventArgs.Empty);
        if (NewestFirst)
        {
            // Mới nhất ở đầu bảng: bỏ dòng của các mục từ "from" ở đầu, chèn lại từ cũ tới mới vào vị trí 0.
            while (Rows.Count > 0 && Rows[0].Index >= from)
            {
                Rows.RemoveAt(0);
            }
            for (var i = from; i < Entries.Count; i++)
            {
                if (filter.Matches(Entries[i]))
                {
                    Rows.Insert(0, new LogRow(Entries[i], i));
                }
            }
        }
        else
        {
            while (Rows.Count > 0 && Rows[^1].Index >= from)
            {
                Rows.RemoveAt(Rows.Count - 1);
            }
            for (var i = from; i < Entries.Count; i++)
            {
                if (filter.Matches(Entries[i]))
                {
                    Rows.Add(new LogRow(Entries[i], i));
                }
            }
        }
        UpdateCounts(filter);
        RowsAppended?.Invoke(this, EventArgs.Empty);
    }

    private LogFilter CurrentFilter() => new(Kinds.Where(k => k.IsChecked).Select(k => k.Kind).ToHashSet(),
        SearchText, UserText, DatabaseText, ClientText, HideOwn);

    private void ApplyFilter()
    {
        var filter = CurrentFilter();
        var entries = Entries;
        var rows = entries.Select((e, i) => (e, i)).Where(x => filter.Matches(x.e)).Select(x => new LogRow(x.e, x.i));
        Rows.ReplaceAll(NewestFirst ? rows.Reverse() : rows);
        UpdateCounts(filter);
        if (IsFollowing)
        {
            ScrollToEndRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateCounts(LogFilter filter)
    {
        var entries = Entries;
        var visible = filter with { Kinds = Enum.GetValues<LogKind>().ToHashSet() };
        var counts = new Dictionary<LogKind, int>();
        foreach (var e in entries)
        {
            if (visible.Matches(e))
            {
                counts[e.Kind] = counts.GetValueOrDefault(e.Kind) + 1;
            }
        }
        foreach (var k in Kinds)
        {
            k.Text = $"{ServerLogParser.KindText(k.Kind)} ({counts.GetValueOrDefault(k.Kind):N0})";
        }
        CountText = entries.Count == 0 ? string.Empty : $"{Rows.Count:N0} / {entries.Count:N0} mục";
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
            var baseName = string.Concat($"{Profile?.Name}_{Path.GetFileNameWithoutExtension(_readFile)}_{DateTime.Now:yyyyMMdd_HHmmss}"
                .Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch == ' ' ? '_' : ch));
            var path = Path.Combine(folder, baseName + extension);
            System.IO.File.WriteAllText(path, format((extension == ".txt" ? Rows.OrderBy(r => r.Index) : Rows.AsEnumerable()).Select(r => r.Entry).ToList()), encoding);
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
