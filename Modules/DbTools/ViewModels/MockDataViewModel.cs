using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DbTools.Core;
using DbTools.Models;
using DbTools.Services;
using Microsoft.Extensions.Logging;

namespace DbTools.ViewModels;

/// <summary>1 cột trên trang Sinh dữ liệu mẫu: thông tin + cách sinh (chỉnh được).</summary>
public sealed partial class MockColumnRow : ObservableObject
{
    /// <summary>Nhãn các cách sinh, cùng thứ tự với <see cref="MockRule"/>.</summary>
    public static IReadOnlyList<string> RuleLabels { get; } =
    [
        "Bỏ qua (DB tự sinh)", "Tăng dần", "Số ngẫu nhiên", "Ngày ngẫu nhiên", "Chọn trong danh sách", "Mẫu chuỗi {n}",
        "Giữ nguyên", "NULL", "true / false", "UUID", "Khoá ngoại",
    ];

    public MockColumnRow(MockSourceColumn source, IEnumerable<string?> samples)
    {
        Source = source;
        var list = samples.ToList();
        SampleText = list.Count == 0 ? "(không có mẫu)"
            : string.Join(" | ", list.Select(v => v ?? "NULL").Distinct().Take(5)) + (list.Distinct().Count() > 5 ? " | ..." : string.Empty);
        var (rule, param) = MockSuggest.For(source.Name, source.Type, source.NotNull, source.Unique, source.DbGenerated, list,
            source.ExistingMax, source.ForeignValues is { Count: > 0 });
        _ruleIndex = (int)rule;
        _param = param;
    }

    public MockSourceColumn Source { get; }
    public string Name => Source.Name;
    public string TypeText => Source.TypeText;
    public string SampleText { get; }

    public string KeyText => string.Join(" · ", new[]
    {
        Source.DbGenerated ? "DB tự sinh" : string.Empty,
        Source.Unique ? "không trùng" : string.Empty,
        Source.ForeignRef.Length > 0 ? $"FK → {Source.ForeignRef} ({Source.ForeignValues?.Count ?? 0} giá trị)" : string.Empty,
        Source.NotNull ? "NOT NULL" : string.Empty,
    }.Where(s => s.Length > 0));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ParamHint), nameof(ParamEnabled))]
    private int _ruleIndex;

    [ObservableProperty]
    private string _param;

    public MockRule Rule => (MockRule)Math.Clamp(RuleIndex, 0, RuleLabels.Count - 1);

    public bool ParamEnabled => Rule is MockRule.Sequence or MockRule.Number or MockRule.DateRange or MockRule.Pick or MockRule.Pattern or MockRule.Constant;

    public string ParamHint => Rule switch
    {
        MockRule.Sequence => "giá trị bắt đầu: 1001 hoặc ORD000124",
        MockRule.Number => "min..max, vd 100..5000 hoặc 0.5..99.99",
        MockRule.DateRange => "yyyy-MM-dd..yyyy-MM-dd",
        MockRule.Pick => "các giá trị cách nhau bằng |, vd 0|1",
        MockRule.Pattern => "chuỗi có {n} = số thứ tự, vd khách {n}",
        MockRule.Constant => "giá trị cho mọi dòng",
        MockRule.ForeignKey => "chọn ngẫu nhiên giá trị đang có ở bảng tham chiếu",
        _ => string.Empty,
    };

    public MockColumnSpec ToSpec() => new(Name, Source.Type, Source.NotNull, Rule, Param ?? string.Empty, Source.ForeignValues);
}

/// <summary>
/// Trang "Sinh dữ liệu mẫu": từ vài dòng mẫu (bảng trong DB hoặc file CSV) sinh thêm N dòng giả theo cách sinh từng cột → file CSV và /
/// hoặc file INSERT. Không ghi gì vào DB. Lựa chọn (số dòng, đầu ra, encoding, tiêu đề, NULL, thư mục) nhớ trong cài đặt.
/// </summary>
public sealed partial class MockDataViewModel : ObservableObject
{
    private const int PreviewRows = 20;
    private readonly ILogger _log = AppLog.For("MockData");
    private MockSourceData? _source;
    private int _seed = Environment.TickCount;

    /// <summary>Bảng chuyển từ trang Thông tin DB (nút Sinh dữ liệu mẫu) - trang đọc khi được mở.</summary>
    public static (ConnectionProfile Profile, string Database, string Table)? Pending { get; set; }

    public ObservableCollection<ConnectionProfile> Profiles => AppState.Profiles;

    // ----- Nguồn mẫu -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FromCsv))]
    private bool _fromDb = true;

    public bool FromCsv
    {
        get => !FromDb;
        set => FromDb = !value;
    }

    [ObservableProperty]
    private ConnectionProfile? _profile;

    public ObservableCollection<string> Databases { get; } = [];

    [ObservableProperty]
    private string _database = string.Empty;

    public ObservableCollection<string> TableNames { get; } = [];

    [ObservableProperty]
    private string _table = string.Empty;

    [ObservableProperty]
    private double _sampleRows = AppState.Settings.MockSampleRows;

    [ObservableProperty]
    private string _csvPath = string.Empty;

    /// <summary>Tên bảng ghi vào INSERT khi nguồn là file CSV (trống = tên file).</summary>
    [ObservableProperty]
    private string _csvTable = string.Empty;

    [ObservableProperty]
    private string _sourceInfo = "Chọn nguồn mẫu rồi bấm Đọc mẫu.";

    public ObservableCollection<MockColumnRow> Columns { get; } = [];

    public bool HasColumns => Columns.Count > 0;

    // ----- Đầu ra -----

    [ObservableProperty]
    private double _rows = AppState.Settings.MockRows;

    [ObservableProperty]
    private bool _outCsv = AppState.Settings.MockCsv;

    [ObservableProperty]
    private bool _outSql = AppState.Settings.MockSql;

    /// <summary>0 UTF-8 có BOM, 1 UTF-8, 2 Shift-JIS.</summary>
    [ObservableProperty]
    private int _encodingIndex = Enum.TryParse<CsvEncoding>(AppState.Settings.MockEncoding, out var e) ? (int)e : 0;

    [ObservableProperty]
    private bool _header = AppState.Settings.MockHeader;

    /// <summary>0 = NULL để ô trống, 1 = ghi chữ NULL.</summary>
    [ObservableProperty]
    private int _nullIndex = AppState.Settings.MockNullAsText ? 1 : 0;

    [ObservableProperty]
    private string _outputFolder = AppState.Settings.MockFolderOrDefault;

    partial void OnSampleRowsChanged(double value) => Save(s => s.MockSampleRows = (int)Math.Clamp(double.IsNaN(value) ? 5 : value, 1, 1000));
    partial void OnRowsChanged(double value) => Save(s => s.MockRows = RowCount);
    partial void OnOutCsvChanged(bool value) => Save(s => s.MockCsv = value);
    partial void OnOutSqlChanged(bool value) => Save(s => s.MockSql = value);
    partial void OnEncodingIndexChanged(int value) => Save(s => s.MockEncoding = ((CsvEncoding)Math.Clamp(value, 0, 2)).ToString());
    partial void OnHeaderChanged(bool value) => Save(s => s.MockHeader = value);
    partial void OnNullIndexChanged(int value) => Save(s => s.MockNullAsText = value == 1);

    partial void OnOutputFolderChanged(string value) =>
        Save(s => s.MockFolder = string.Equals(value.Trim(), Path.Combine(s.DumpFolder, "DuLieuMau"), StringComparison.OrdinalIgnoreCase) ? string.Empty : value.Trim());

    private static void Save(Action<AppSettings> change)
    {
        change(AppState.Settings);
        AppState.SaveSettings();
    }

    public int RowCount => (int)Math.Clamp(double.IsNaN(Rows) ? 100 : Rows, 1, AppSettings.MockMaxRows);

    private CsvOptions Csv => new((CsvEncoding)Math.Clamp(EncodingIndex, 0, 2), Header, NullIndex == 1);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(LoadSampleCommand), nameof(PreviewCommand), nameof(GenerateCommand))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    private string _status = "Đọc mẫu → xem / chỉnh cách sinh từng cột → Xem trước → Sinh file. Không ghi gì vào DB.";

    [ObservableProperty]
    private string _previewText = string.Empty;

    private string? _lastFolder;

    public MockDataViewModel()
    {
        Profile = AppState.LastProfile;
        Columns.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasColumns));
    }

    partial void OnProfileChanged(ConnectionProfile? value)
    {
        Database = value?.Database ?? string.Empty;
        Databases.Clear();
        TableNames.Clear();
    }

    partial void OnDatabaseChanged(string value) => TableNames.Clear();

    public void EnsureProfile()
    {
        if (Profile is null || !Profiles.Contains(Profile))
        {
            Profile = AppState.LastProfile;
        }
    }

    /// <summary>Mở từ trang Thông tin DB: điền sẵn server / database / bảng rồi đọc mẫu.</summary>
    public async Task TakePendingAsync()
    {
        if (Pending is not { } p)
        {
            return;
        }
        Pending = null;
        FromDb = true;
        Profile = p.Profile;
        Database = p.Database;
        Table = p.Table;
        await LoadSampleAsync();
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

    public async Task LoadTableNamesAsync()
    {
        if (Profile is not { } profile || Database.Trim().Length == 0 || TableNames.Count > 0)
        {
            return;
        }
        try
        {
            using var cts = new CancellationTokenSource(Pg.Within(20));
            await using var c = await PgInfo.OpenAsync(profile, Database.Trim(), cts.Token);
            foreach (var t in await PgInfo.TablesAsync(c, cts.Token))
            {
                TableNames.Add($"{t.Schema}.{t.Name}");
            }
        }
        catch (Exception ex)
        {
            _log.LogInformation("Không đọc được danh sách bảng: {Error}", Pg.Describe(ex));
        }
    }

    [RelayCommand]
    private async Task BrowseCsvAsync()
    {
        if (await Shell.PickFileAsync(".csv", ".tsv", ".txt", "*") is { } path)
        {
            CsvPath = path;
            FromDb = false;
            await LoadSampleAsync();
        }
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        if (await Shell.PickFolderAsync() is { } folder)
        {
            OutputFolder = folder;
        }
    }

    [RelayCommand]
    private void OpenOutput()
    {
        var folder = _lastFolder ?? OutputFolder.Trim();
        if (Directory.Exists(folder))
        {
            Shell.Open(folder);
        }
        else
        {
            Status = $"Chưa có thư mục {folder} (tự tạo khi Sinh file).";
        }
    }

    // ----- Đọc mẫu -----

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task LoadSampleAsync()
    {
        IsBusy = true;
        try
        {
            if (FromDb)
            {
                if (Profile is not { } profile || Database.Trim().Length == 0 || Table.Trim().Length == 0)
                {
                    Status = "Chọn server, database và bảng (schema.bảng).";
                    return;
                }
                using var cts = new CancellationTokenSource(Pg.Within(1 * 60));
                _source = await MockSource.FromTableAsync(profile, Database.Trim(), Table.Trim(), AppState.Settings.MockSampleRows, cts.Token);
            }
            else
            {
                var path = CsvPath.Trim().Trim('"');
                if (!File.Exists(path))
                {
                    Status = "Chọn file CSV mẫu (dòng đầu là tên cột).";
                    return;
                }
                _source = await Task.Run(() => MockSource.FromCsv(path, CsvTable));
            }
            Columns.Clear();
            for (var i = 0; i < _source.Columns.Count; i++)
            {
                var index = i;
                Columns.Add(new MockColumnRow(_source.Columns[i], _source.Samples.Select(r => r[index])));
            }
            _seed = Environment.TickCount;
            PreviewText = string.Empty;
            SourceInfo = $"{_source.Title}: {_source.Columns.Count} cột, {_source.Samples.Count} dòng mẫu"
                + (_source.UniqueSets.Count > 0 ? $" · không trùng: {string.Join("; ", _source.UniqueSets.Select(s => string.Join(", ", s)))}" : string.Empty)
                + $" · INSERT vào {_source.TableSql}";
            Status = "Đã đọc mẫu - xem cách sinh từng cột (app đề xuất, chỉnh được) rồi Xem trước.";
        }
        catch (Exception ex)
        {
            Status = "Không đọc được mẫu: " + (ex is MockError ? ex.Message : Pg.Describe(ex));
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ----- Xem trước / sinh file -----

    private List<MockColumnSpec> Specs() => Columns.Select(c => c.ToSpec()).ToList();

    /// <summary>20 dòng đầu (cùng seed với lúc Sinh file → đúng 20 dòng đầu của file), dạng CSV.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void Preview()
    {
        if (_source is null)
        {
            Status = "Chưa có mẫu - bấm Đọc mẫu.";
            return;
        }
        try
        {
            var specs = Specs();
            var rows = MockGenerator.Generate(specs, Math.Min(PreviewRows, RowCount), _seed, _source.UniqueSets);
            var sw = new StringWriter();
            MockOutput.WriteCsv(sw, specs.Where(s => s.Rule != MockRule.Skip).Select(s => s.Name).ToList(), rows, Csv with { Header = true });
            PreviewText = sw.ToString();
            Status = $"Xem trước {rows.Count} dòng đầu (file sẽ có {RowCount:N0} dòng).";
        }
        catch (MockError ex)
        {
            PreviewText = string.Empty;
            Status = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task GenerateAsync()
    {
        if (_source is not { } source)
        {
            Status = "Chưa có mẫu - bấm Đọc mẫu.";
            return;
        }
        if (!OutCsv && !OutSql)
        {
            Status = "Chọn ít nhất 1 đầu ra: CSV hoặc SQL INSERT.";
            return;
        }
        var folder = OutputFolder.Trim();
        var specs = Specs();
        var count = RowCount;
        var csv = Csv;
        var name = $"{PgArgs.SafeFileName(source.TableSql.Replace("\"", string.Empty))}_{count}dong_{DateTime.Now:yyyyMMdd_HHmmss}";
        var (outCsv, outSql) = (OutCsv, OutSql);
        var written = new List<string>();
        IsBusy = true;
        Status = $"Đang sinh {count:N0} dòng...";
        try
        {
            Directory.CreateDirectory(folder);
            await Task.Run(() =>
            {
                if (outCsv)
                {
                    var path = PgArgs.UniquePath(folder, name + ".csv");
                    written.Add(path);
                    using var w = new StreamWriter(path, false, MockOutput.EncodingOf(csv.Encoding));
                    MockOutput.WriteCsv(w, specs.Where(s => s.Rule != MockRule.Skip).Select(s => s.Name).ToList(),
                        MockGenerator.Stream(specs, count, _seed, source.UniqueSets), csv);
                }
                if (outSql)
                {
                    var path = PgArgs.UniquePath(folder, name + ".sql");
                    written.Add(path);
                    using var w = new StreamWriter(path, false, new UTF8Encoding(false));
                    MockOutput.WriteInsert(w, source.TableSql, specs, MockGenerator.Stream(specs, count, _seed, source.UniqueSets),
                        $"DbTools - {count:N0} dòng dữ liệu mẫu cho {source.TableSql} ({source.Title}), sinh lúc {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                }
            });
            _lastFolder = folder;
            Status = $"Đã sinh {count:N0} dòng: {string.Join(", ", written.Select(Path.GetFileName))} trong {folder}";
            _log.LogInformation("Sinh dữ liệu mẫu {Table}: {Count} dòng → {Files}", source.TableSql, count, string.Join(", ", written));
        }
        catch (Exception ex)
        {
            foreach (var path in written)
            {
                try
                {
                    File.Delete(path); // file dở dang
                }
                catch (Exception)
                {
                }
            }
            Status = (ex is MockError ? ex.Message : "Không sinh được: " + ex.Message) + " - chưa ghi file nào.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
