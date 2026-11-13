using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MeRon.Core;
using MeRon.Core.Models;
using MeRon.Core.Orders;
using MeRon.Services;
using Microsoft.Extensions.Logging;

namespace MeRon.ViewModels;

/// <summary>1 dòng của bảng nháp Nhập đơn hàng loạt (= 1 dòng chữ đã ghép khách / món). <paramref name="reconcile"/> = kết
/// quả so khớp với đơn trong MeRon cùng ngày giao (null = chưa so được).</summary>
public sealed partial class BatchRowViewModel(BatchLineMatch match, bool isChecked, ReconcileEntry? reconcile) : ObservableObject
{
    public BatchLineMatch Match { get; } = match;

    public ReconcileEntry? Reconcile { get; } = reconcile;

    [ObservableProperty]
    private bool _isChecked = isChecked;

    public bool CanSave => Match.CanSave;

    /// <summary>Số dòng phía trên giống hệt dòng này (<see cref="BatchReconciler.DuplicateLines"/>), null = không trùng.</summary>
    public int? DuplicateOf { get; init; }

    /// <summary>Lưu được nhưng nên xem lại: cảnh báo khi đọc dòng hoặc trùng dòng phía trên - mặc định không tick.</summary>
    public bool HasWarnings => Match.Warnings.Count > 0 || DuplicateOf is not null;

    public bool NeedsReview => !Match.CanSave || HasWarnings || Reconcile?.Kind == ReconcileKind.Different;

    /// <summary>Dòng ổn không có hàng nút sửa: các nút hiện khi rê chuột.</summary>
    public bool EditOnHover => !NeedsReview;

    /// <summary>Nút Chọn khách: dòng chưa khớp khách nào, hoặc đã khớp mà có thể ghép nhầm. Dòng khớp nhiều khách đã có ô chọn riêng.</summary>
    public bool CanChooseCustomer => NeedsNewCustomer || Match.Customer is not null;

    /// <summary>Nhãn so khớp: ✔ đã có / ≠ khác / chưa có trong MeRon (ngày giao đang chọn).</summary>
    public string ReconcileText => Reconcile?.Kind switch
    {
        ReconcileKind.Matched => "✓ Đã có trong MeRon (khớp)",
        ReconcileKind.Different => "≠ Khác đơn trong MeRon: " + string.Join("; ", Reconcile.Differences),
        ReconcileKind.OnlyInDraft => "Chưa có trong MeRon",
        _ => string.Empty,
    };

    public bool IsMatched => Reconcile?.Kind == ReconcileKind.Matched;

    public bool IsDifferent => Reconcile?.Kind == ReconcileKind.Different;

    public string LineText => $"dòng {Match.Line.Number}";

    /// <summary>Khách / món theo lựa chọn đã ghim (popup Sửa đơn, ô chọn trên thẻ) - nhãn "Đã ghim" + nút Bỏ ghim.</summary>
    public bool IsPinned => Match.Pinned;

    public string CustomerText => Match.Customer is { } c
        ? string.IsNullOrWhiteSpace(c.Address) ? c.Name ?? string.Empty : $"{c.Name} ({c.Address})"
        : Match.Line.Customer;

    public string ItemsText => string.Join(", ", Match.Items.Select(i =>
        i.IsFree ? $"{i.Label} (món tự do {VnFormat.Money(i.FreePrice)})"
        : i.IsResolved ? i.Label
        : $"{i.Label} ⚠"));

    public string TotalText => Match.CanSave ? VnFormat.Money(Match.Total) : "?";

    public bool Paid => Match.Line.Paid;

    public string ProblemText => string.Join("  ·  ", Match.Problems);

    public string WarningText => string.Join("  ·  ", DuplicateOf is { } line
        ? Match.Warnings.Prepend($"Trùng dòng {line} (dán 2 lần?) - khách đặt 2 đơn giống nhau thật thì tự tick")
        : Match.Warnings);

    /// <summary>Không khách nào khớp → nút "Thêm khách" (popup, điền sẵn tên + số căn hộ).</summary>
    public bool NeedsNewCustomer => Match.Customer is null && Match.CustomerCandidates.Count == 0 && Match.Line.Error is null;

    /// <summary>Nhiều khách khớp → ô chọn khách.</summary>
    public IReadOnlyList<Customer> CustomerChoices => Match.Customer is null ? Match.CustomerCandidates : [];

    /// <summary>Mọi món (sắp theo tên) - ô chọn món cho chữ lạ không có gợi ý nào (vd "pana" khi chưa có chữ viết tắt).</summary>
    public IReadOnlyList<Product> AllProducts { get; init; } = [];

    /// <summary>Món đầu tiên chưa ghép được → ô chọn món: nhiều món khớp (vd "bún" → Bún chả / Bún chả cá) thì chỉ các món
    /// đó, không món nào khớp thì cả danh sách (gõ chữ đầu để nhảy tới).</summary>
    public int ItemChoiceIndex => Match.Items.ToList().FindIndex(i => !i.IsResolved);

    public IReadOnlyList<Product> ItemChoices => ItemChoiceIndex < 0 ? []
        : Match.Items[ItemChoiceIndex].Candidates.Count > 1 ? Match.Items[ItemChoiceIndex].Candidates : AllProducts;

    public string ItemChoicePlaceholder => ItemChoiceIndex < 0 ? string.Empty
        : Match.Items[ItemChoiceIndex].Candidates.Count > 1
            ? $"Chọn món cho \"{Match.Items[ItemChoiceIndex].Name}\"..."
            : $"\"{Match.Items[ItemChoiceIndex].Name}\" là món nào?...";

    public string AccessibleName => $"Dòng {Match.Line.Number}: {CustomerText} - {ItemsText}";
}

/// <summary>
/// Nhập đơn hàng loạt: ô chữ (chép từ sổ ghi tay qua Claude, tin nhắn Zalo, gõ tay) là nguồn gốc - tự lưu vào file cạnh
/// database, còn sau khi tắt app; bảng nháp là kết quả đọc + ghép với khách / món hiện có (<see cref="BatchOrderMatcher"/>),
/// tự đọc lại khi ngừng gõ (trang gọi <see cref="RefreshIfChanged"/>), Làm mới đọc lại ngay. Lưu = tạo các đơn đã tick, bỏ các dòng đó khỏi ô chữ, nhớ lô để Hoàn tác.
/// </summary>
public sealed partial class BatchOrdersViewModel : ObservableObject
{
    public const string DraftFileName = "nhap-don-hang-loat.txt";

    private static string FolderPath => AppServices.Database.Folder;

    /// <summary>Tick người dùng tự đổi (theo nội dung dòng) - giữ qua các lần Làm mới.</summary>
    private readonly Dictionary<string, bool> _checkOverrides = new(StringComparer.Ordinal);

    private List<BatchRowViewModel> _all = [];

    /// <summary>Ghim theo chữ dòng (<see cref="BatchPin"/>), lưu cạnh ô chữ nháp.</summary>
    private readonly BatchPins _pins = BatchPins.Load(Path.Combine(FolderPath, BatchPins.FileName));

    /// <summary>Chữ cũ của các dòng đã ghim vừa bị sửa tay, đang chờ trả lời "giữ ghim / bỏ ghim" - chưa được dọn.</summary>
    private readonly HashSet<string> _pendingPinRaws = new(StringComparer.Ordinal);

    /// <summary>Dòng đã ghim vừa bị sửa tay chữ (gõ trong ô chữ): trang hỏi hoặc tự bỏ ghim theo Cài đặt rồi gọi
    /// <see cref="ResolvePinnedEdits"/>.</summary>
    public event Action<IReadOnlyList<BatchPinEdit>>? PinnedLinesEdited;

    public ObservableCollection<BatchRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    private string _text = ReadFile(DraftFileName) ?? string.Empty;

    [ObservableProperty]
    private string _abbreviationsText = ReadFile(Abbreviations.FileName) ?? Abbreviations.DefaultText;

    [ObservableProperty]
    private DateTimeOffset? _day = DateTimeOffset.Now.Date;

    [ObservableProperty]
    private bool _onlyNeedsReview;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string _error = string.Empty;

    [ObservableProperty]
    private int _checkedCount;

    /// <summary>Lô vừa lưu (đơn như lúc lưu + dòng chữ gốc) - Hoàn tác xoá các đơn còn nguyên và trả dòng về ô chữ.</summary>
    private List<BatchSavedOrder> _lastBatch = [];

    /// <summary>Ghim của các dòng trong lô vừa lưu - Hoàn tác trả dòng về ô chữ kèm ghim.</summary>
    private readonly Dictionary<string, BatchPin> _lastBatchPins = new(StringComparer.Ordinal);

    [ObservableProperty]
    private string _undoText = string.Empty;

    /// <summary>Ngày của dòng "Ngày | ..." ở lần đọc trước - chỉ đổi ô Ngày giao khi dòng đó đổi (không đè ngày tự chọn).</summary>
    private DateOnly? _lastTextDay;

    /// <summary>Kết quả so khớp của lần Làm mới gần nhất (cả đơn chỉ có trong MeRon - không có thẻ trên bảng nháp).</summary>
    private IReadOnlyList<ReconcileEntry> _reconcile = [];

    [ObservableProperty]
    private string _reconcileButtonText = "So khớp";

    /// <summary>Bảng tổng hợp nút So khớp: 5 nhóm (nhóm rỗng bỏ), mỗi nhóm 1 dòng / đơn.</summary>
    public IReadOnlyList<(string Title, IReadOnlyList<string> Lines)> ReconcileReport()
    {
        static string Items(IEnumerable<(long Quantity, string? Name)> items) => string.Join(", ", items.Select(i => $"{i.Quantity} {i.Name}"));
        string Book(ReconcileEntry e) =>
            $"dòng {e.Draft!.Line.Number} · {(e.Draft.Customer?.Name ?? e.Draft.Line.Customer)}: "
            + string.Join(", ", e.Draft.Items.Select(i => i.Label));
        static string App(Order o) =>
            $"{o.UserName ?? "(không có khách)"}: {Items(o.Items.Select(i => (i.Quantity, i.SnapshotName)))} · {VnFormat.Money(o.Total)} · {OrderStatusText.Label(o.Status)}";

        var groups = new (string Title, IEnumerable<string> Lines)[]
        {
            ("≠ Khác đơn trong MeRon", _reconcile.Where(e => e.Kind == ReconcileKind.Different).Select(e => $"{Book(e)}  →  {string.Join("; ", e.Differences)}")),
            ("Chỉ có trong sổ (chưa nhập vào MeRon)", _reconcile.Where(e => e.Kind == ReconcileKind.OnlyInDraft).Select(Book)),
            ("Chỉ có trong MeRon (sổ không ghi)", _reconcile.Where(e => e.Kind == ReconcileKind.OnlyInDatabase).Select(e => App(e.Order!))),
            ("Chưa so được (dòng chưa ghép xong khách / món)", _reconcile.Where(e => e.Kind == ReconcileKind.NotComparable).Select(e => $"dòng {e.Draft!.Line.Number} · {e.Draft.Line.Raw}")),
            ("✓ Khớp", _reconcile.Where(e => e.Kind == ReconcileKind.Matched).Select(Book)),
        };
        return groups.Select(g => (Title: g.Title, Lines: (IReadOnlyList<string>)g.Lines.ToList()))
            .Where(g => g.Lines.Count > 0)
            .Select(g => ($"{g.Title} ({g.Lines.Count})", g.Lines))
            .ToList();
    }

    /// <summary>Bảng tổng hợp dạng chữ (nút Chép kết quả).</summary>
    public string ReconcileReportText() =>
        $"So khớp sổ với MeRon - ngày giao {DeliveryDay:dd/MM/yyyy}\n\n" +
        string.Join("\n\n", ReconcileReport().Select(g => g.Title + "\n" + string.Join("\n", g.Lines.Select(l => "- " + l))));

    partial void OnTextChanged(string value) => WriteFile(DraftFileName, value);

    partial void OnAbbreviationsTextChanged(string value) => WriteFile(Abbreviations.FileName, value);

    partial void OnOnlyNeedsReviewChanged(bool value) => ShowRows();

    private bool _refreshing;

    /// <summary>Chữ ở lần đọc gần nhất - xem <see cref="RefreshIfChanged"/>.</summary>
    private string _readText = string.Empty;

    private string _readAbbreviations = string.Empty;

    /// <summary>Đổi Ngày giao → kiểm tra trùng đơn lại theo ngày mới.</summary>
    partial void OnDayChanged(DateTimeOffset? value)
    {
        if (!_refreshing)
        {
            Refresh();
        }
    }

    public DateOnly DeliveryDay => DateOnly.FromDateTime((Day ?? DateTimeOffset.Now).DateTime);

    /// <summary>Toàn bộ khách, cho nút Chọn khách trên dòng không khớp khách nào.</summary>
    public IReadOnlyList<CustomerOption> Customers { get; private set; } = [];

    /// <summary>Đọc lại ô chữ + khách / món / chữ viết tắt mới nhất (sau khi thêm khách, sửa chữ, thêm món...).</summary>
    public void Refresh()
    {
        Error = string.Empty;
        _refreshing = true;
        var editedPins = new List<BatchPinEdit>();
        _readText = Text;
        _readAbbreviations = AbbreviationsText;
        try
        {
            var parsed = BatchOrderParser.Parse(Text, DateOnly.FromDateTime(DateTime.Today));

            // Dòng đã tự bỏ tick mà sửa chữ thì vẫn bỏ tick: đừng để dòng người dùng không muốn lưu tự tick lại.
            foreach (var (oldRaw, newRaw) in BatchOrderParser.EditedLines(_all.Select(r => r.Match.Line).ToList(), parsed.Lines))
            {
                if (_checkOverrides.TryGetValue(oldRaw, out var kept) && !kept)
                {
                    _checkOverrides[newRaw] = false;
                }
                if (_pins.Get(oldRaw) is not null && _pins.Get(newRaw) is null && parsed.Lines.FirstOrDefault(l => l.Raw == newRaw) is { } edited)
                {
                    _pendingPinRaws.Add(oldRaw);
                    editedPins.Add(new BatchPinEdit(edited.Number, oldRaw, newRaw));
                }
            }
            if (_pins.Prune(parsed.Lines.Select(l => l.Raw), _pendingPinRaws))
            {
                SavePins();
            }

            if (parsed.Day is { } textDay && textDay != _lastTextDay)
            {
                Day = textDay.ToDateTime(TimeOnly.MinValue);
            }
            _lastTextDay = parsed.Day;

            var customers = AppServices.MasterData.GetCustomers();
            Customers = CustomerOption.From(customers);
            var products = AppServices.MasterData.GetProducts();
            var sortedProducts = products.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var abbreviations = new Abbreviations(AbbreviationsText);
            var matches = parsed.Lines.Select(line => BatchOrderMatcher.Match(line, customers, products, abbreviations, _pins.Get(line.Raw))).ToList();

            // So khớp với đơn (không tính Huỷ) cùng ngày giao: dòng đã có / khác trong MeRon thì không tự tick (tránh lưu trùng).
            var orders = AppServices.Orders.Query(new OrderFilter(null, DeliveryDay, DeliveryDay))
                .Where(o => o.Status != OrderStatus.Cancelled).ToList();
            var entries = BatchReconciler.Compare(matches, orders);
            _reconcile = entries;
            var duplicates = BatchReconciler.DuplicateLines(matches);
            _all = matches.Select(match =>
            {
                var line = match.Line;
                var entry = entries.FirstOrDefault(e => ReferenceEquals(e.Draft, match));
                int? duplicateOf = duplicates.TryGetValue(match, out var first) ? first : null;
                var fresh = match.Warnings.Count == 0 && duplicateOf is null && entry?.Kind is not (ReconcileKind.Matched or ReconcileKind.Different);
                // Dòng trùng: tick tay theo số dòng (2 dòng cùng chữ dùng chung khoá chữ nên tick dòng này sẽ tick cả dòng kia).
                var overrideKey = duplicateOf is null ? line.Raw : $"{line.Number}\u0001{line.Raw}";
                var isChecked = match.CanSave && (_checkOverrides.TryGetValue(overrideKey, out var forced) ? forced : fresh);
                var row = new BatchRowViewModel(match, isChecked, entry) { AllProducts = sortedProducts, DuplicateOf = duplicateOf };
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(BatchRowViewModel.IsChecked))
                    {
                        _checkOverrides[overrideKey] = row.IsChecked;
                        UpdateCounts();
                    }
                };
                return row;
            }).ToList();
        }
        catch (Exception ex)
        {
            AppLog.For("BatchOrders").LogError(ex, "Đọc bảng nháp lỗi");
            Error = $"Không đọc được dữ liệu: {ex.Message}";
            _all = [];
            _reconcile = [];
        }
        finally
        {
            _refreshing = false;
        }
        ShowRows();
        if (editedPins.Count > 0)
        {
            PinnedLinesEdited?.Invoke(editedPins);
        }
    }

    /// <summary>Trả lời cho các dòng đã ghim bị sửa tay: <paramref name="keepPinned"/> = trả chữ dòng về như cũ, giữ ghim (dòng
    /// đã bị sửa tiếp / xoá thì thôi, bỏ ghim); không thì bỏ ghim, đọc theo chữ mới.</summary>
    public void ResolvePinnedEdits(IReadOnlyList<BatchPinEdit> edits, bool keepPinned)
    {
        var text = Text;
        foreach (var edit in edits)
        {
            _pendingPinRaws.Remove(edit.OldRaw);
            if (keepPinned && BatchOrderParser.HasLine(text, edit.Number, edit.NewRaw))
            {
                text = BatchOrderParser.ReplaceLine(text, edit.Number, edit.OldRaw);
            }
            else
            {
                _pins.Remove(edit.OldRaw);
            }
        }
        SavePins();
        Text = text;
        Refresh();
    }

    /// <summary>Ghim hiện có của dòng (chưa ghim → rỗng) - để chọn thêm khách / món mà không mất phần đã ghim.</summary>
    public BatchPin PinOf(BatchRowViewModel row) => _pins.Get(row.Match.Line.Raw) ?? BatchPin.Empty;

    /// <summary>Nút Bỏ ghim: đọc lại dòng theo chữ.</summary>
    public void Unpin(BatchRowViewModel row)
    {
        if (_pins.Remove(row.Match.Line.Raw))
        {
            SavePins();
        }
        Refresh();
    }

    /// <summary>Chữ khách ghi vào dòng khi chọn khách (<see cref="BatchOrderParser.CustomerText"/>).</summary>
    public string CustomerLineText(long id, string? name, string? address) =>
        BatchOrderParser.CustomerText(id, name, address, Customers.Select(c => (c.Id, (string?)c.Name)));

    private void SavePins()
    {
        try
        {
            _pins.Save(Path.Combine(FolderPath, BatchPins.FileName));
        }
        catch (Exception ex)
        {
            AppLog.For("BatchOrders").LogError(ex, "Không lưu được {File}", BatchPins.FileName);
        }
    }

    /// <summary>Ô chữ / chữ viết tắt khác lần đọc trước thì mới đọc lại: gõ rồi xoá về như cũ, hay chữ do chính trang này viết
    /// lại, thì không dựng lại danh sách.</summary>
    public void RefreshIfChanged()
    {
        if (!BatchOrderParser.SameText(Text, _readText) || !BatchOrderParser.SameText(AbbreviationsText, _readAbbreviations))
        {
            Refresh();
        }
    }

    private void ShowRows()
    {
        Rows.Clear();
        foreach (var row in _all.Where(r => !OnlyNeedsReview || r.NeedsReview))
        {
            Rows.Add(row);
        }
        UpdateCounts();
    }

    private void UpdateCounts()
    {
        CheckedCount = _all.Count(r => r.IsChecked && r.CanSave);
        var problems = _all.Count(r => !r.CanSave);
        var warnings = _all.Count(r => r.CanSave && r.HasWarnings);
        var inApp = _all.Count(r => r.IsMatched || r.IsDifferent);
        Summary = _all.Count == 0
            ? "Chưa có dòng nào - dán chữ vào ô trên"
            : $"{_all.Count} dòng  ·  {_all.Count(r => r.CanSave && !r.HasWarnings && !r.IsMatched && !r.IsDifferent)} sẵn sàng"
              + (inApp > 0 ? $"  ·  {inApp} đã có trong MeRon" : string.Empty)
              + (warnings > 0 ? $"  ·  {warnings} nên xem lại" : string.Empty)
              + (problems > 0 ? $"  ·  {problems} cần sửa" : string.Empty)
              + $"  ·  đã tick {CheckedCount}";
        ReconcileButtonText = $"So khớp ngày {DeliveryDay:dd/MM}";
    }

    /// <summary>Lưu các dòng đã tick thành đơn (ngày giao = ô Ngày giao, CK = đã thanh toán). Trả về số đơn đã lưu.</summary>
    public int SaveChecked()
    {
        var rows = _all.Where(r => r.IsChecked && r.CanSave).ToList();
        if (rows.Count == 0)
        {
            return 0;
        }
        var products = AppServices.MasterData.GetProducts().ToDictionary(p => p.Id);
        var saved = new List<BatchSavedOrder>();
        var savedLines = new List<int>();
        _lastBatchPins.Clear();
        foreach (var row in rows)
        {
            Order? order = null;
            var error = AppServices.Try("Lưu đơn", () =>
            {
                var id = AppServices.Orders.Save(row.Match.ToDraft(DeliveryDay), products);
                order = AppServices.Orders.Get(id);
            });
            if (error is not null || order is null)
            {
                Error = $"Dòng {row.Match.Line.Number} ({row.CustomerText}): {error ?? "không đọc lại được đơn vừa lưu"}";
                break;
            }
            saved.Add(new BatchSavedOrder(order, row.Match.Line.Raw));
            if (_pins.Get(row.Match.Line.Raw) is { } pin)
            {
                _lastBatchPins[row.Match.Line.Raw] = pin;
            }
            savedLines.Add(row.Match.Line.Number);
        }

        if (saved.Count > 0)
        {
            _lastBatch = saved;
            UndoText = $"Hoàn tác lô vừa lưu ({saved.Count} đơn)";
            var paid = rows.Take(saved.Count).Count(r => r.Paid);
            Message = $"Đã lưu {saved.Count} đơn giao ngày {DeliveryDay:dd/MM/yyyy}" + (paid > 0 ? $" ({paid} đơn đã thanh toán)" : string.Empty)
                      + " - xem ở trang Đặt hàng.";
            Text = BatchOrderParser.RemoveLines(Text, savedLines);
            AppLog.For("BatchOrders").LogInformation("Nhập hàng loạt: lưu {Count} đơn", saved.Count);
        }
        Refresh();
        return saved.Count;
    }

    /// <summary>Xét lô vừa lưu với dữ liệu hiện tại (gọi trước hộp thoại Hoàn tác): đơn nào còn nguyên, đơn nào đã đổi.</summary>
    public BatchUndoPlan PlanUndo() => BatchUndo.Plan(_lastBatch, AppServices.Orders.Get);

    /// <summary>Xoá các đơn còn nguyên như lúc lưu (<paramref name="plan"/>), trả các dòng đó về cuối ô chữ; đơn đã sửa /
    /// thanh toán / huỷ / không còn thì giữ, dòng không trả về (tránh lưu lại thành trùng).</summary>
    public void UndoLastBatch(BatchUndoPlan plan)
    {
        var error = AppServices.Try("Hoàn tác lô", () =>
        {
            foreach (var item in plan.Delete)
            {
                AppServices.Orders.Delete(item.Saved.Id);
            }
        });
        if (error is not null)
        {
            Error = error;
            return;
        }
        if (plan.Delete.Count > 0)
        {
            foreach (var item in plan.Delete.Where(d => _lastBatchPins.ContainsKey(d.Raw)))
            {
                _pins.Set(item.Raw, _lastBatchPins[item.Raw]);
            }
            SavePins();
            Text = (Text.TrimEnd() + "\n" + string.Join("\n", plan.Delete.Select(b => b.Raw))).TrimStart('\n');
        }
        Message = (plan.Delete.Count > 0 ? $"Đã xoá {plan.Delete.Count} đơn vừa lưu, các dòng đã trả về ô chữ." : "Không xoá đơn nào.")
                  + (plan.Keep.Count > 0 ? $" Giữ lại {plan.Keep.Count} đơn đã đổi sau khi lưu." : string.Empty);
        AppLog.For("BatchOrders").LogInformation("Hoàn tác lô: xoá {Deleted} đơn, giữ {Kept} đơn đã đổi", plan.Delete.Count, plan.Keep.Count);
        _lastBatch = [];
        _lastBatchPins.Clear();
        UndoText = string.Empty;
        Refresh();
    }

    /// <summary>Thêm 1 dòng "chữ = tên món" vào bảng Chữ viết tắt (tự lưu).</summary>
    public void AddAbbreviation(string word, string fullName) =>
        AbbreviationsText = (AbbreviationsText.TrimEnd() + $"\n{word} = {fullName}").TrimStart('\n');

    /// <summary>Viết lại dòng của <paramref name="row"/> (chọn khách / món trên bảng nháp, popup Sửa đơn) rồi đọc lại.
    /// <paramref name="pin"/> = ghim cho dòng mới (ghim của dòng cũ bỏ đi). Chữ mới có thể y hệt chữ cũ (vd chọn 1 trong 2 khách
    /// trùng tên) - khi đó chỉ ghim là đổi.</summary>
    public void RewriteLine(BatchRowViewModel row, string newLine, BatchPin? pin = null)
    {
        if (!BatchOrderParser.HasLine(Text, row.Match.Line.Number, row.Match.Line.Raw))
        {
            Refresh();
            Error = "Ô chữ vừa đổi nên chưa thay được dòng này - xem lại danh sách rồi chọn lại.";
            return;
        }
        if (pin is not null)
        {
            _pins.Remove(row.Match.Line.Raw);
            _pins.Set(newLine, pin);
            SavePins();
        }
        Text = BatchOrderParser.ReplaceLine(Text, row.Match.Line.Number, newLine);
        Refresh();
    }

    /// <summary>Câu lệnh mẫu dán vào Claude (claude.ai / app điện thoại) cùng ảnh sổ: chép đúng mẫu trang này đọc được.</summary>
    public string ClaudePrompt() =>
        "Tôi gửi ảnh chụp trang sổ ghi đơn hàng viết tay (bán đồ ăn cho cư dân chung cư).\n" +
        "Mỗi dòng sổ là 1 đơn: bên trái là khách (số căn hộ và/hoặc tên, vd \"706 Mỹ Ly\", \"W1 Tuấn Chung\", \"12a09\"), bên phải là các món nối bằng dấu \"+\".\n\n" +
        "Hãy chép lại MỖI ĐƠN 1 DÒNG theo đúng mẫu sau, không thêm gì khác:\n" +
        "khách | số lượng tên món; số lượng tên món | giá nếu có ghi | CK nếu có ghi\n\n" +
        "Quy tắc:\n" +
        "- Giữ nguyên chữ viết tắt như trong sổ (vd \"3 sc uống\"), tôi tự đổi.\n" +
        "- Giá viết như trong sổ (vd 45k). Không ghi giá thì để trống.\n" +
        "- Chữ nào đọc không chắc thì thêm dấu ? ngay sau chữ đó, vd \"5 pana?\".\n" +
        "- Dòng bị gạch bỏ thì bỏ qua.\n" +
        "- Dòng đầu tiên ghi ngày của trang sổ, dạng: Ngày | 29/10\n\n" +
        "Các chữ viết tắt hay gặp (để đọc cho đúng):\n" + AbbreviationsText.Trim();

    private static string? ReadFile(string name)
    {
        try
        {
            var path = Path.Combine(FolderPath, name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex)
        {
            AppLog.For("BatchOrders").LogError(ex, "Không đọc được {File}", name);
            return null;
        }
    }

    private static void WriteFile(string name, string value)
    {
        try
        {
            File.WriteAllText(Path.Combine(FolderPath, name), value);
        }
        catch (Exception ex)
        {
            AppLog.For("BatchOrders").LogError(ex, "Không lưu được {File}", name);
        }
    }
}
