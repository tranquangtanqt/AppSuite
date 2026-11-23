using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DbTools.Core;
using DbTools.Models;
using DbTools.Services;
using Microsoft.Extensions.Logging;
using Windows.ApplicationModel.DataTransfer;

namespace DbTools.ViewModels;

public sealed record InfoLine(string Label, string Value);

public sealed class SchemaRow(SchemaStat s)
{
    public string Schema => s.Schema;
    public string Owner => s.Owner;
    public string Tables => s.Tables.ToString("N0");
    public string Views => s.Views.ToString("N0");
    public string Functions => s.Functions.ToString("N0");
    public string Triggers => s.Triggers.ToString("N0");
    public string Sequences => s.Sequences.ToString("N0");
    public string Size => BackupViewModel.FormatSize(s.SizeBytes);
}

/// <summary>1 dòng tab Bảng. Số dòng: ước lượng (≈) tới khi bấm Đếm chính xác.</summary>
public sealed partial class TableRow(TableItem t) : ObservableObject
{
    public TableItem Item { get; } = t;
    public string FullName => $"{Item.Schema}.{Item.Name}";
    public string Name => Item.Name;
    public string Schema => Item.Schema;
    public string KindText => Item.Kind switch { "p" => "phân vùng", "f" => "bảng ngoài", _ => Item.IsPartition ? "phân vùng con" : string.Empty };
    public string Size => BackupViewModel.FormatSize(Item.TotalBytes);
    public string Columns => Item.Columns.ToString();
    public string PrimaryKey => Item.HasPrimaryKey ? "✓" : "—";
    public string Comment => Item.Comment;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Rows), nameof(RowCount))]
    private long? _exactRows;

    public long RowCount => ExactRows ?? Item.RowEstimate;
    public string Rows => ExactRows is { } n ? n.ToString("N0") : "≈ " + Item.RowEstimate.ToString("N0");

    public string Tip => $"{FullName}{(Item.Comment.Length > 0 ? " - " + Item.Comment : string.Empty)}\n"
        + $"Bảng {BackupViewModel.FormatSize(Item.TableBytes)} + index {BackupViewModel.FormatSize(Item.IndexBytes)} (tổng gồm cả TOAST {Size})\n"
        + $"VACUUM gần nhất: {Item.LastVacuum?.ToString("yyyy-MM-dd HH:mm") ?? "chưa"} · ANALYZE: {Item.LastAnalyze?.ToString("yyyy-MM-dd HH:mm") ?? "chưa"}";
}

/// <summary>1 cột trong chi tiết bảng.</summary>
public sealed class ColumnRow(ColumnInfo c, bool primary, string? foreign)
{
    public string Number => c.Number.ToString();
    public string Name => c.Name;
    public string Type => c.Type + (c.Collation.Length > 0 ? " COLLATE " + c.Collation : string.Empty);
    public string Null => c.NotNull ? "NOT NULL" : string.Empty;
    public string Default => c.DefaultText;
    public string Comment => c.Comment;
    public string Key => (primary ? "PK" : string.Empty) + (foreign is null ? string.Empty : (primary ? " · " : string.Empty) + "FK → " + foreign);
}

public sealed class IndexRow(IndexInfo i)
{
    public string Name => i.Name;
    public string Kind => i.Primary ? "khoá chính" : i.Unique ? "duy nhất" : string.Empty;
    public string Size => BackupViewModel.FormatSize(i.SizeBytes);
    public string Definition => i.Definition;
}

public sealed class ConstraintRow(ConstraintInfo c)
{
    public string Name => c.Name;
    public string Kind => c.TypeText;
    public string Definition => c.Definition;
}

public sealed class TriggerRow(TriggerInfo t)
{
    public string Name => t.Name;
    public string State => t.Disabled ? "ĐANG TẮT" : "bật";
    public string Function => t.Function;
    public string Definition => t.Definition;
}

/// <summary>1 dòng tab Đối tượng khác. Kết quả Tìm trong mã nguồn: <paramref name="matches"/> = các chỗ khớp (Info = tóm tắt).</summary>
public sealed class ObjectRow(DbObjectItem o, IReadOnlyList<SourceMatch>? matches = null)
{
    public DbObjectItem Item { get; } = o;
    public IReadOnlyList<SourceMatch> Matches { get; } = matches ?? [];
    public string FullName => matches is null || o.Kind is "view" or "matview" ? $"{o.Schema}.{o.Name}" : $"{o.Schema}.{o.Name}{o.Info}";
    public string Info => matches is null ? o.Info : $"{KindText(o.Kind)} · {SourceSearch.Summary(Matches)}";
    public string Comment => o.Comment;

    private static string KindText(string kind) => kind switch { "matview" => "mat. view", "procedure" => "procedure", "function" => "hàm", _ => kind };
}

/// <summary>1 dòng tab Phụ thuộc (chi tiết bảng).</summary>
public sealed class DependentRow(DependentItem d)
{
    public DependentItem Item { get; } = d;
    public string Kind => d.Kind switch
    {
        DependentKind.View => "View",
        DependentKind.ForeignKey => "Khoá ngoại",
        DependentKind.Trigger => "Trigger",
        DependentKind.Sequence => "Sequence",
        DependentKind.TrackedFunction => "Hàm",
        DependentKind.Function => "Hàm (mã nguồn)",
        _ => "Trigger khác",
    };
    public string Name => d.Name;
    public string Relation => d.Relation;
    /// <summary>Chắc chắn (catalog ghi nhận) hay dò chữ trong mã nguồn.</summary>
    public string Certainty => d.Kind is DependentKind.Function or DependentKind.IndirectTrigger ? "dò mã nguồn" : "catalog";
}

/// <summary>
/// Trang "Thông tin DB" (tra cứu cấu trúc thay DBeaver, chỉ đọc catalog): Tổng quan (bản server, dung lượng, encoding, schema +
/// số đối tượng), Bảng (lọc / sắp, chi tiết: cột, index, ràng buộc, trigger, DDL), Đối tượng khác (view, hàm, trigger, sequence,
/// extension + định nghĩa).
/// </summary>
public sealed partial class DbInfoViewModel : ObservableObject
{
    public const string AllSchemas = "(Tất cả schema)";
    private static readonly string[] ObjectKinds = ["view", "function", "trigger", "sequence", "extension", "search"];
    private readonly ILogger _log = AppLog.For("DbInfo");
    private List<TableRow> _allTables = [];
    private List<DbObjectItem> _allObjects = [];
    private ConnectionProfile? _loadedProfile;

    /// <summary>Server + database của lần Đọc gần nhất (chi tiết bảng mở trên đó).</summary>
    public ConnectionProfile? LoadedProfile => _loadedProfile;

    public string LoadedDatabase => _loadedDatabase;
    private string _loadedDatabase = string.Empty;

    public ObservableCollection<ConnectionProfile> Profiles => AppState.Profiles;

    [ObservableProperty]
    private ConnectionProfile? _profile;

    public ObservableCollection<string> Databases { get; } = [];

    [ObservableProperty]
    private string _database = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand), nameof(OpenDetailCommand), nameof(CountRowsCommand), nameof(CountAllCommand), nameof(SearchCommand))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    private string _status = "Chọn server + database rồi bấm Đọc (chỉ đọc catalog, không thay đổi gì).";

    /// <summary>0 Tổng quan, 1 Bảng, 2 Đối tượng khác.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFilter), nameof(ShowTableList), nameof(ShowTableDetail))]
    private int _tab;

    public bool ShowFilter => Tab != 0 && !(Tab == 1 && IsDetailOpen);
    public bool ShowTableList => Tab == 1 && !IsDetailOpen;
    public bool ShowTableDetail => Tab == 1 && IsDetailOpen;

    // ----- Tổng quan -----

    public ObservableCollection<InfoLine> Overview { get; } = [];
    public ObservableCollection<SchemaRow> Schemas { get; } = [];

    // ----- Lọc chung (Bảng, Đối tượng khác) -----

    public ObservableCollection<string> SchemaOptions { get; } = [AllSchemas];

    [ObservableProperty]
    private string _selectedSchema = AllSchemas;

    [ObservableProperty]
    private string _nameFilter = string.Empty;

    /// <summary>0 tên, 1 dung lượng (lớn trước), 2 số dòng (nhiều trước).</summary>
    [ObservableProperty]
    private int _sortIndex;

    partial void OnSelectedSchemaChanged(string value) => ApplyFilters();
    partial void OnNameFilterChanged(string value) => ApplyFilters();
    partial void OnSortIndexChanged(int value) => ApplyFilters();

    // ----- Bảng -----

    public ObservableCollection<TableRow> Tables { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDetailCommand))]
    private TableRow? _selectedTable;

    [ObservableProperty]
    private string _tablesSummary = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFilter), nameof(ShowTableList), nameof(ShowTableDetail))]
    [NotifyCanExecuteChangedFor(nameof(CountRowsCommand))]
    private bool _isDetailOpen;

    [ObservableProperty]
    private string _detailTitle = string.Empty;

    [ObservableProperty]
    private string _detailInfo = string.Empty;

    /// <summary>0 Cột, 1 Index, 2 Ràng buộc, 3 Trigger, 4 DDL, 5 Phụ thuộc.</summary>
    [ObservableProperty]
    private int _detailTab;

    public ObservableCollection<ColumnRow> DetailColumns { get; } = [];
    public ObservableCollection<IndexRow> DetailIndexes { get; } = [];
    public ObservableCollection<ConstraintRow> DetailConstraints { get; } = [];
    public ObservableCollection<TriggerRow> DetailTriggers { get; } = [];

    [ObservableProperty]
    private string _ddl = string.Empty;

    // Nhãn tab chi tiết kèm số lượng, vd "Cột (12)".
    [ObservableProperty]
    private string _columnsTab = "Cột";

    [ObservableProperty]
    private string _indexesTab = "Index";

    [ObservableProperty]
    private string _constraintsTab = "Ràng buộc";

    [ObservableProperty]
    private string _triggersTab = "Trigger";

    // Phụ thuộc: đối tượng dùng bảng + tóm tắt ảnh hưởng khi xoá / sửa bảng
    public ObservableCollection<DependentRow> DetailDependents { get; } = [];

    [ObservableProperty]
    private string _dependentsTab = "Phụ thuộc";

    [ObservableProperty]
    private string _dependentsImpact = string.Empty;

    [ObservableProperty]
    private DependentRow? _selectedDependent;

    [ObservableProperty]
    private string _dependentDetail = "Chọn 1 dòng để xem định nghĩa / các dòng nhắc tới bảng.";

    partial void OnSelectedDependentChanged(DependentRow? value) =>
        DependentDetail = value is null ? "Chọn 1 dòng để xem định nghĩa / các dòng nhắc tới bảng."
        : value.Item.Detail.Length > 0 ? value.Item.Detail : value.Relation;

    /// <summary>Định nghĩa mọi view / hàm của lần Đọc gần nhất (đọc khi cần lần đầu, Đọc lại thì bỏ).</summary>
    private List<DbObjectItem>? _sources;

    private async Task<List<DbObjectItem>> SourcesAsync(Npgsql.NpgsqlConnection c, CancellationToken ct) =>
        _sources ??= await PgInfo.SourcesAsync(c, ct);

    private TableRow? _detailRow;

    // ----- Đối tượng khác -----

    /// <summary>0 View, 1 Hàm / procedure, 2 Trigger, 3 Sequence, 4 Extension, 5 Tìm trong mã nguồn.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearchKind))]
    private int _objectKind;

    partial void OnObjectKindChanged(int value) => ApplyFilters();

    // ----- Tìm trong mã nguồn (view / hàm / procedure) -----

    public bool IsSearchKind => ObjectKind == 5;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _searchWholeWord;

    [ObservableProperty]
    private bool _searchCaseSensitive;

    [ObservableProperty]
    private string _searchSummary = string.Empty;

    private List<ObjectRow> _searchResults = [];

    /// <summary>Chỗ khớp đầu tiên của dòng kết quả đang chọn - trang bôi đen + cuộn tới trong ô Định nghĩa.</summary>
    [ObservableProperty]
    private SourceMatch? _definitionHighlight;

    private bool CanSearch => IsIdle && _loadedProfile is not null;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        var term = SearchText.Trim();
        if (term.Length == 0 || _loadedProfile is null)
        {
            Status = _loadedProfile is null ? "Bấm Đọc trước." : "Gõ chữ cần tìm.";
            return;
        }
        IsBusy = true;
        try
        {
            using var cts = new CancellationTokenSource(Pg.Within(2 * 60));
            if (_sources is null)
            {
                Status = "Đang đọc mã nguồn mọi view / hàm...";
                await using var c = await PgInfo.OpenAsync(_loadedProfile, _loadedDatabase, cts.Token);
                await SourcesAsync(c, cts.Token);
            }
            _searchResults = _sources!
                .Select(s => new ObjectRow(s, SourceSearch.Find(s.Definition, term, SearchWholeWord, SearchCaseSensitive)))
                .Where(r => r.Matches.Count > 0)
                .ToList();
            var places = _searchResults.Sum(r => r.Matches.Count);
            SearchSummary = $"\"{term}\": {places:N0} chỗ trong {_searchResults.Count:N0} / {_sources!.Count:N0} view + hàm";
            ApplyFilters();
            Status = SearchSummary + (Objects.Count < _searchResults.Count ? $" ({Objects.Count:N0} khớp bộ lọc schema / tên)." : ".");
        }
        catch (Exception ex)
        {
            Status = "Không tìm được: " + Pg.Describe(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public ObservableCollection<ObjectRow> Objects { get; } = [];

    [ObservableProperty]
    private ObjectRow? _selectedObject;

    [ObservableProperty]
    private string _objectDefinition = "Chọn 1 dòng để xem định nghĩa.";

    [ObservableProperty]
    private string _objectsSummary = string.Empty;

    partial void OnSelectedObjectChanged(ObjectRow? value) => _ = ShowDefinitionAsync(value);

    public DbInfoViewModel()
    {
        Profile = Profiles.FirstOrDefault(p => p.Name == AppState.Settings.LastProfile) ?? Profiles.FirstOrDefault();
    }

    partial void OnProfileChanged(ConnectionProfile? value)
    {
        Database = value?.Database ?? string.Empty;
        Databases.Clear();
    }

    public void EnsureProfile()
    {
        if (Profile is null || !Profiles.Contains(Profile))
        {
            Profile = Profiles.FirstOrDefault(p => p.Name == AppState.Settings.LastProfile) ?? Profiles.FirstOrDefault();
        }
    }

    public async Task LoadDatabasesAsync()
    {
        if (Profile is not { } profile)
        {
            return;
        }
        try
        {
            using var cts = new CancellationTokenSource(Pg.Within(10));
            foreach (var db in (await PgAdmin.ListDatabasesAsync(profile, cts.Token)).Where(db => !Databases.Contains(db)))
            {
                Databases.Add(db);
            }
        }
        catch (Exception ex)
        {
            _log.LogInformation("Không đọc được danh sách database: {Error}", Pg.Describe(ex));
        }
    }

    // ----- Đọc -----

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task LoadAsync()
    {
        if (Profile is not { } profile || Database.Trim().Length == 0)
        {
            Status = "Chọn server (hồ sơ kết nối) và database.";
            return;
        }
        var database = Database.Trim();
        IsBusy = true;
        Status = $"Đang đọc {profile.Host}:{profile.Port}/{database}...";
        try
        {
            using var cts = new CancellationTokenSource(Pg.Within(2 * 60));
            await using var c = await PgInfo.OpenAsync(profile, database, cts.Token);
            var overview = await PgInfo.OverviewAsync(c, cts.Token);
            var schemas = await PgInfo.SchemasAsync(c, cts.Token);
            var tables = await PgInfo.TablesAsync(c, cts.Token);
            var objects = await PgInfo.ObjectsAsync(c, cts.Token);
            _loadedProfile = profile;
            _loadedDatabase = database;

            Overview.Clear();
            Overview.Add(new InfoLine("Database", $"{overview.Database} trên {profile.Host}:{profile.Port}"));
            Overview.Add(new InfoLine("PostgreSQL", overview.Version));
            Overview.Add(new InfoLine("Dung lượng", BackupViewModel.FormatSize(overview.SizeBytes)));
            Overview.Add(new InfoLine("Encoding", $"{overview.Encoding} · collate {overview.Collate} · ctype {overview.Ctype}"));
            Overview.Add(new InfoLine("Owner", overview.Owner));
            Overview.Add(new InfoLine("Server chạy từ", overview.ServerStart?.ToString("yyyy-MM-dd HH:mm") ?? "-"));
            Overview.Add(new InfoLine("Phiên đang kết nối", $"{overview.Sessions} (gồm phiên của DbTools)"));
            Overview.Add(new InfoLine("Đối tượng", $"{tables.Count:N0} bảng · {objects.Count(o => o.Kind is "view" or "matview"):N0} view · "
                + $"{objects.Count(o => o.Kind is "function" or "procedure"):N0} hàm · {objects.Count(o => o.Kind == "trigger"):N0} trigger · "
                + $"{objects.Count(o => o.Kind == "sequence"):N0} sequence · {objects.Count(o => o.Kind == "extension"):N0} extension"));
            if (overview.Comment.Length > 0)
            {
                Overview.Add(new InfoLine("Ghi chú", overview.Comment));
            }
            Schemas.Clear();
            foreach (var s in schemas)
            {
                Schemas.Add(new SchemaRow(s));
            }

            var keep = SelectedSchema;
            SchemaOptions.Clear();
            SchemaOptions.Add(AllSchemas);
            foreach (var s in schemas)
            {
                SchemaOptions.Add(s.Schema);
            }
            SelectedSchema = SchemaOptions.Contains(keep) ? keep : AllSchemas;

            _allTables = tables.Select(t => new TableRow(t)).ToList();
            _allObjects = objects;
            _sources = null;
            _searchResults = [];
            SearchSummary = string.Empty;
            IsDetailOpen = false;
            ApplyFilters();
            Status = $"Đã đọc {overview.Database} ({BackupViewModel.FormatSize(overview.SizeBytes)}, {tables.Count:N0} bảng) lúc {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            Status = "Không đọc được: " + Pg.Describe(ex);
            _log.LogInformation("Đọc thông tin DB lỗi: {Error}", Pg.Describe(ex));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool Matches(string schema, string name)
    {
        var filter = NameFilter.Trim();
        return (SelectedSchema == AllSchemas || SelectedSchema == schema)
            && (filter.Length == 0 || name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || $"{schema}.{name}".Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyFilters()
    {
        IEnumerable<TableRow> tables = _allTables.Where(t => Matches(t.Schema, t.Name));
        tables = SortIndex switch
        {
            1 => tables.OrderByDescending(t => t.Item.TotalBytes),
            2 => tables.OrderByDescending(t => t.RowCount),
            _ => tables.OrderBy(t => t.Schema, StringComparer.Ordinal).ThenBy(t => t.Name, StringComparer.Ordinal),
        };
        Tables.Clear();
        foreach (var t in tables)
        {
            Tables.Add(t);
        }
        TablesSummary = _allTables.Count == 0 ? string.Empty
            : $"{Tables.Count:N0} / {_allTables.Count:N0} bảng · {BackupViewModel.FormatSize(Tables.Sum(t => t.Item.TotalBytes))}";

        var kind = ObjectKinds[Math.Clamp(ObjectKind, 0, ObjectKinds.Length - 1)];
        Objects.Clear();
        if (kind == "search")
        {
            foreach (var r in _searchResults.Where(r => Matches(r.Item.Schema, r.Item.Name)))
            {
                Objects.Add(r);
            }
            ObjectsSummary = SearchSummary;
        }
        else
        {
            foreach (var o in _allObjects.Where(o => KindOf(o) == kind && (kind == "extension" ? NameMatches(o.Name) : Matches(o.Schema, o.Name))))
            {
                Objects.Add(new ObjectRow(o));
            }
            ObjectsSummary = _allObjects.Count == 0 ? string.Empty : $"{Objects.Count:N0} / {_allObjects.Count(o => KindOf(o) == kind):N0}";
        }
        CountAllCommand.NotifyCanExecuteChanged();
    }

    private bool NameMatches(string name) => NameFilter.Trim().Length == 0 || name.Contains(NameFilter.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string KindOf(DbObjectItem o) => o.Kind switch
    {
        "matview" => "view",
        "procedure" => "function",
        var k => k,
    };

    // ----- Chi tiết bảng -----

    private bool CanOpenDetail => IsIdle && SelectedTable is not null;

    [RelayCommand(CanExecute = nameof(CanOpenDetail))]
    public async Task OpenDetailAsync()
    {
        if (SelectedTable is not { } row || _loadedProfile is null)
        {
            return;
        }
        IsBusy = true;
        try
        {
            using var cts = new CancellationTokenSource(Pg.Within(1 * 60));
            await using var c = await PgInfo.OpenAsync(_loadedProfile, _loadedDatabase, cts.Token);
            var d = await PgInfo.TableDetailAsync(c, row.Item.Oid, cts.Token);
            _detailRow = row;
            DetailTitle = $"{d.Schema}.{d.Name}";
            DetailInfo = string.Join(" · ", new[]
            {
                row.KindText.Length > 0 ? row.KindText : "bảng",
                $"{row.Rows} dòng",
                $"{row.Size} (bảng {BackupViewModel.FormatSize(row.Item.TableBytes)}, index {BackupViewModel.FormatSize(row.Item.IndexBytes)})",
                $"owner {d.Owner}",
                d.PartitionKey, d.PartitionOf, d.Inherits.Length > 0 ? "kế thừa " + d.Inherits : string.Empty,
            }.Where(s => s.Length > 0)) + (d.Comment.Length > 0 ? Environment.NewLine + d.Comment : string.Empty);
            Fill(DetailColumns, d.Columns.Select(col => new ColumnRow(col, d.PrimaryKeyColumns.Contains(col.Name), d.ForeignKeyColumns.GetValueOrDefault(col.Name))));
            Fill(DetailIndexes, d.Indexes.Select(i => new IndexRow(i)));
            Fill(DetailConstraints, d.Constraints.Where(k => k.Type != "n").Select(k => new ConstraintRow(k)));
            Fill(DetailTriggers, d.Triggers.Select(t => new TriggerRow(t)));
            ColumnsTab = $"Cột ({d.Columns.Count})";
            IndexesTab = $"Index ({d.Indexes.Count})";
            ConstraintsTab = $"Ràng buộc ({DetailConstraints.Count})";
            TriggersTab = $"Trigger ({d.Triggers.Count})";
            Ddl = TableDdl.Build(d);
            Status = $"Đang tìm đối tượng dùng {DetailTitle}...";
            var dependents = await PgInfo.DependentsAsync(c, row.Item.Oid, d.Schema, d.Name, await SourcesAsync(c, cts.Token), cts.Token);
            Fill(DetailDependents, dependents.Select(x => new DependentRow(x)));
            DependentsTab = $"Phụ thuộc ({dependents.Count})";
            DependentsImpact = string.Join(Environment.NewLine, DependencyImpact.Describe(DetailTitle, dependents));
            SelectedDependent = null;
            IsDetailOpen = true;
            Status = $"Chi tiết {DetailTitle}: {d.Columns.Count} cột, {d.Indexes.Count} index, {DetailConstraints.Count} ràng buộc, {d.Triggers.Count} trigger, "
                + $"{dependents.Count} đối tượng phụ thuộc.";
        }
        catch (Exception ex)
        {
            Status = "Không đọc được chi tiết bảng: " + Pg.Describe(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var i in items)
        {
            target.Add(i);
        }
    }

    [RelayCommand]
    private void CloseDetail() => IsDetailOpen = false;

    private bool CanCountRows => IsIdle && IsDetailOpen;

    /// <summary>SELECT count(*) - bảng lớn có thể lâu.</summary>
    [RelayCommand(CanExecute = nameof(CanCountRows))]
    private async Task CountRowsAsync()
    {
        if (_detailRow is not { } row)
        {
            return;
        }
        await CountAsync([row]);
        DetailInfo = DetailInfo.Replace("≈ " + row.Item.RowEstimate.ToString("N0"), row.Rows);
    }

    private bool CanCountAll => IsIdle && Tables.Count > 0;

    /// <summary>Đếm chính xác các bảng đang hiện trong danh sách (sau khi lọc).</summary>
    [RelayCommand(CanExecute = nameof(CanCountAll))]
    private Task CountAllAsync() => CountAsync(Tables.ToList());

    private async Task CountAsync(List<TableRow> rows)
    {
        if (_loadedProfile is null || rows.Count == 0)
        {
            return;
        }
        IsBusy = true;
        try
        {
            await using var c = await PgInfo.OpenAsync(_loadedProfile, _loadedDatabase, CancellationToken.None);
            var done = 0;
            foreach (var row in rows.Where(r => r.Item.Kind != "f"))
            {
                Status = $"Đang đếm {row.FullName} ({++done}/{rows.Count})...";
                row.ExactRows = await PgInfo.CountRowsAsync(c, row.Schema, row.Name, CancellationToken.None);
            }
            Status = rows.Count == 1 ? $"{rows[0].FullName}: {rows[0].Rows} dòng (đếm chính xác)." : $"Đã đếm chính xác {done} bảng.";
        }
        catch (Exception ex)
        {
            Status = "Không đếm được: " + Pg.Describe(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CopyDdl() => Copy(Ddl, $"Đã copy DDL của {DetailTitle}.");

    // ----- Đối tượng khác -----

    private async Task ShowDefinitionAsync(ObjectRow? row)
    {
        if (row is null)
        {
            ObjectDefinition = "Chọn 1 dòng để xem định nghĩa.";
            return;
        }
        if (row.Item.Definition.Length > 0 || _loadedProfile is null)
        {
            ObjectDefinition = row.Item.Definition;
            DefinitionHighlight = row.Matches.Count > 0 ? row.Matches[0] : null;
            if (row.Matches.Count > 0)
            {
                Status = $"{row.FullName}: {row.Matches.Count} chỗ khớp, dòng " + string.Join(", ", row.Matches.Select(m => m.Line).Distinct().Take(20))
                    + (row.Matches.Select(m => m.Line).Distinct().Count() > 20 ? "..." : ".");
            }
            return;
        }
        DefinitionHighlight = null;
        try
        {
            using var cts = new CancellationTokenSource(Pg.Within(30));
            await using var c = await PgInfo.OpenAsync(_loadedProfile, _loadedDatabase, cts.Token);
            var text = await PgInfo.DefinitionAsync(c, row.Item, cts.Token);
            if (SelectedObject == row)
            {
                ObjectDefinition = text;
            }
        }
        catch (Exception ex)
        {
            ObjectDefinition = "-- Không đọc được định nghĩa: " + Pg.Describe(ex);
        }
    }

    [RelayCommand]
    private void CopyDefinition() => Copy(ObjectDefinition, $"Đã copy định nghĩa {SelectedObject?.FullName}.");

    private void Copy(string text, string message)
    {
        if (text.Length == 0)
        {
            return;
        }
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        Status = message;
    }
}
