using System.ComponentModel;
using MeRon.Core.Models;
using MeRon.Core.Orders;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace MeRon.Views;

/// <summary>Nhập đơn hàng loạt - xem <see cref="BatchOrdersViewModel"/>. Trang cache: giữ bảng nháp + lô vừa lưu khi chuyển trang.</summary>
public sealed partial class BatchOrdersPage : Page
{
    public BatchOrdersViewModel ViewModel { get; } = new();

    /// <summary>Ngừng gõ chừng này thì đọc lại: mỗi phím tính lại từ đầu, nên đang gõ liên tục không dựng lại danh sách.</summary>
    private readonly DispatcherTimer _autoRefresh = new() { Interval = TimeSpan.FromMilliseconds(600) };

    private bool _pasting;

    public BatchOrdersPage()
    {
        InitializeComponent();
        _autoRefresh.Tick += (_, _) =>
        {
            _autoRefresh.Stop();
            ViewModel.RefreshIfChanged();
        };
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.PinnedLinesEdited += edits => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => PinnedLinesEdited(edits));
    }

    /// <summary>Sửa tay chữ của dòng đã ghim: Cài đặt "Hỏi trước khi bỏ ghim" bật thì hỏi, không thì tự bỏ ghim. Esc = bỏ ghim
    /// (giữ nguyên chữ vừa gõ).</summary>
    private async void PinnedLinesEdited(IReadOnlyList<BatchPinEdit> edits)
    {
        if (App.MainWindow?.MenuSettings.AskBeforeUnpin != true || XamlRoot is null)
        {
            ViewModel.ResolvePinnedEdits(edits, keepPinned: false);
            return;
        }
        var lines = string.Join("\n", edits.Select(e => $"Dòng {e.Number}:\n   trước: {e.OldRaw}\n   giờ:    {e.NewRaw}"));
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Sửa dòng đã ghim?",
            Content = SharedUI.Helpers.DialogContent.Scrollable(
                $"{(edits.Count == 1 ? "Dòng này" : $"{edits.Count} dòng này")} đã chọn khách / món trong popup Sửa đơn hoặc ô chọn trên thẻ (đã ghim). Bạn vừa sửa tay chữ:\n\n{lines}\n\n"
                + "Bỏ ghim: đọc theo chữ mới.\nGiữ ghim: trả chữ dòng về như trước, giữ khách / món đã chọn.", 520),
            PrimaryButtonText = "Bỏ ghim, đọc chữ mới",
            SecondaryButtonText = "Giữ ghim, trả chữ về",
            DefaultButton = ContentDialogButton.Primary,
        };
        var result = await dialog.ShowAsync();
        ViewModel.ResolvePinnedEdits(edits, keepPinned: result == ContentDialogResult.Secondary);
    }

    /// <summary>Ô Tick hết đơn sẵn sàng: bấm là CheckBox tự đổi trạng thái - đặt lại theo ViewModel (giá trị không đổi thì x:Bind không đặt lại).</summary>
    private void CheckAll_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleAll();
        CheckAllBox.IsChecked = ViewModel.AllChecked;
    }

    private void Unpin_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            ViewModel.Unpin(row);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(BatchOrdersViewModel.Text) or nameof(BatchOrdersViewModel.AbbreviationsText)))
        {
            return;
        }
        _autoRefresh.Stop();
        if (_pasting)
        {
            _pasting = false;
            ViewModel.RefreshIfChanged();
        }
        else
        {
            _autoRefresh.Start();
        }
    }

    /// <summary>Sự kiện Paste chạy trước khi chữ vào ô: đánh dấu, đọc lại ngay ở lần chữ đổi kế tiếp.</summary>
    private void PasteBox_Paste(object sender, TextControlPasteEventArgs e) => _pasting = true;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Message = string.Empty;
        ViewModel.Refresh(); // khách / món có thể vừa đổi ở trang khác
    }

    public static bool Positive(int count) => count > 0;

    public static string SaveText(int count) => count > 0 ? $"Lưu {count} đơn đã tick" : "Lưu đơn";

    public static Visibility VisibleIfAny(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public static string CustomerLabel(string? name, string? address) => string.IsNullOrWhiteSpace(address) ? name ?? string.Empty : $"{name} ({address})";

    /// <summary>Nhãn "Đã thanh toán" (dòng có CK) cùng màu nhãn trạng thái ở thẻ đơn.</summary>
    public static Brush PaidSoft(bool paid) => Ui.StatusSoft(OrderStatus.Completed);

    public static Brush PaidSolid(bool paid) => Ui.StatusSolid(OrderStatus.Completed);

    /// <summary>Nhãn so khớp: xanh lá = đã có (khớp), cam = khác đơn trong MeRon, xám = chưa có.</summary>
    public static Brush ReconcileBrush(bool matched, bool different) =>
        matched ? Ui.StatusSolid(OrderStatus.Completed)
        : (Brush)Application.Current.Resources[different ? "SystemFillColorCautionBrush" : "TextFillColorSecondaryBrush"];

    /// <summary>Viền thẻ: đỏ = chưa lưu được, cam = nên xem lại, thường = sẵn sàng.</summary>
    public static Brush RowBorder(bool canSave, bool needsReview) => (Brush)Application.Current.Resources[
        !canSave ? "SystemFillColorCriticalBrush" : needsReview ? "SystemFillColorCautionBrush" : "CardStrokeColorDefaultBrush"];

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Message = string.Empty;
        ViewModel.Refresh();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshIfChanged(); // Ctrl+S ngay sau khi gõ: lưu theo chữ mới nhất, không theo danh sách cũ
        var count = ViewModel.CheckedCount;
        if (count == 0)
        {
            return;
        }
        if (await Dialogs.ConfirmAsync(XamlRoot, "Lưu đơn hàng loạt",
                $"Tạo {count} đơn giao ngày {ViewModel.DeliveryDay:dd/MM/yyyy}? Các dòng đã lưu sẽ được bỏ khỏi ô chữ (Hoàn tác được ngay sau đó).",
                $"Lưu {count} đơn"))
        {
            ViewModel.SaveChecked();
        }
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        BatchUndoPlan plan;
        try
        {
            plan = ViewModel.PlanUndo();
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageAsync(XamlRoot, "Hoàn tác lô vừa lưu", $"Không đọc được các đơn vừa lưu: {ex.Message}");
            return;
        }
        var kept = plan.Keep.Count == 0 ? string.Empty
            : $"\n\nGiữ lại {plan.Keep.Count} đơn đã đổi sau khi lưu (không xoá, dòng không trả về ô chữ):\n"
              + string.Join("\n", plan.Keep.Select(k => $"- Đơn #{k.Order.Saved.Id} {k.Order.Saved.UserName}: {k.Reason}"));
        if (plan.Delete.Count == 0)
        {
            await Dialogs.ShowMessageAsync(XamlRoot, "Hoàn tác lô vừa lưu", "Không còn đơn nào xoá được." + kept);
            ViewModel.UndoLastBatch(plan);
            return;
        }
        if (await Dialogs.ConfirmAsync(XamlRoot, "Hoàn tác lô vừa lưu",
                $"Xoá {plan.Delete.Count} đơn vừa lưu và trả các dòng về ô chữ?" + kept, $"Xoá {plan.Delete.Count} đơn"))
        {
            ViewModel.UndoLastBatch(plan);
        }
    }

    private static BatchRowViewModel? RowOf(object sender) => (sender as FrameworkElement)?.Tag as BatchRowViewModel;

    /// <summary>Khách chưa có: popup Thêm khách điền sẵn - từ có số (706, W1, 12a09) vào Địa chỉ, còn lại vào Tên.</summary>
    private async void AddCustomer_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row)
        {
            return;
        }
        var words = row.Match.Line.Customer.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var address = string.Join(" ", words.Where(w => w.Any(char.IsAsciiDigit)));
        var name = string.Join(" ", words.Where(w => !w.Any(char.IsAsciiDigit)));
        if (await CustomerForm.ShowAsync(XamlRoot, null, name.Length > 0 ? name : row.Match.Line.Customer, address.Length > 0 ? address : null) is not null)
        {
            ViewModel.Refresh();
        }
    }

    /// <summary>Chữ không khớp khách nào (đọc sai, viết tắt...) hoặc ghép nhầm khách: tìm trong toàn bộ khách rồi thay vào dòng. Dùng ô tìm như form
    /// đơn thay cho ComboBox vì danh sách khách dài. Viết lại dòng sau khi khung đóng: dòng cũ (có nút đang mở khung) sẽ bị thay.</summary>
    private void ChooseCustomer_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row)
        {
            return;
        }
        var box = new AutoSuggestBox { Width = 300, PlaceholderText = "Gõ tên hoặc số điện thoại rồi chọn", QueryIcon = new SymbolIcon(Symbol.Find) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, "Tìm khách");
        var flyout = new Flyout { Content = box };
        CustomerOption? picked = null;
        CustomerSuggest.Attach(box, () => ViewModel.Customers, () => picked, customer =>
        {
            if (customer is not null)
            {
                picked = customer;
                flyout.Hide();
            }
        });
        // Ô tìm trống tự mở danh sách gợi ý, Esc mặc định không đóng được khung.
        box.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Escape)
            {
                args.Handled = true;
                flyout.Hide();
            }
        };
        flyout.Opened += (_, _) => box.Focus(FocusState.Programmatic);
        flyout.Closed += (_, _) =>
        {
            if (picked is { } customer) // như AfterComboClosed: nút mở khung nằm trên thẻ sắp bị dựng lại
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    () => PickCustomer(row, customer.Id, customer.Name, customer.Address));
            }
        };
        flyout.ShowAt((FrameworkElement)sender);
    }

    /// <summary>
    /// Chọn trong ô Chọn khách / Chọn món trên thẻ: viết lại dòng sẽ dựng lại cả bảng nháp, xoá luôn ô đang chọn. Làm ngay trong
    /// SelectionChanged (chọn bằng chuột: ô còn đang đóng danh sách, trả tiêu điểm về chính nó) thì WinUI ném COMException
    /// "Element not found" / "Unspecified error" và app tắt. Đợi danh sách đóng hẳn rồi mới làm, ở lượt sau của hàng đợi giao diện.
    /// </summary>
    private void AfterComboClosed(ComboBox combo, Action action)
    {
        void Run() => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => action());
        if (!combo.IsDropDownOpen)
        {
            Run();
            return;
        }
        void Closed(object? s, object e)
        {
            combo.DropDownClosed -= Closed;
            Run();
        }
        combo.DropDownClosed += Closed;
    }

    private void CustomerChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RowOf(sender) is { } row && sender is ComboBox { SelectedItem: Customer customer } combo)
        {
            AfterComboClosed(combo, () => PickCustomer(row, customer.Id, customer.Name, customer.Address));
        }
    }

    /// <summary>Chọn khách cho dòng: ghi tên (trùng tên thì kèm địa chỉ) + ghim khách theo id, giữ ghim món đã có.</summary>
    private void PickCustomer(BatchRowViewModel row, long id, string? name, string? address) =>
        ViewModel.RewriteLine(row, BatchOrderParser.Format(row.Match.Line, customer: ViewModel.CustomerLineText(id, name, address)),
            ViewModel.PinOf(row).WithCustomer(id));

    private void ItemChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RowOf(sender) is { } row && sender is ComboBox { SelectedItem: Product { Name: { } name } product } combo)
        {
            AfterComboClosed(combo, () => ChooseItem(row, product.Id, name));
        }
    }

    /// <summary>Chọn món cho chữ chưa ghép được. Chữ lạ 1 từ, không món nào khớp (vd "pana") → hỏi "Nhớ: pana = Panacota?" để
    /// lần sau tự hiểu (thêm vào bảng Chữ viết tắt). Chữ khớp nhiều món (vd "bún") thì không hỏi: thêm "bún = Bún chả cá" sẽ đổi
    /// cả "bún chả" thành sai.</summary>
    private async void ChooseItem(BatchRowViewModel row, long productId, string name)
    {
        var index = row.ItemChoiceIndex;
        var item = row.Match.Items[index];
        var word = item.Source.Name.Trim();
        if (item.Candidates.Count == 0 && !word.Contains(' ') && Core.TextSearch.Fold(word) != Core.TextSearch.Fold(name)
            && await Dialogs.ConfirmAsync(XamlRoot, "Nhớ chữ viết tắt?",
                $"Lần sau tự hiểu \"{word}\" là \"{name}\"? (thêm dòng \"{word} = {name}\" vào bảng Chữ viết tắt - sửa / xoá được ở đó)", "Nhớ"))
        {
            ViewModel.AddAbbreviation(word, name);
        }
        ViewModel.RewriteLine(row, BatchOrderParser.Format(row.Match.Line, itemIndex: index, itemName: name), ViewModel.PinOf(row).WithItem(index, productId));
    }

    private static StackPanel? HoverActions(object row) =>
        (row as Grid)?.Children.OfType<StackPanel>().FirstOrDefault(p => p.Name == nameof(HoverActions));

    private static bool InUse(StackPanel actions) =>
        actions.Children.OfType<Button>().Any(b => b.IsPointerOver || b.FocusState != FocusState.Unfocused);

    private void Row_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (HoverActions(sender) is { } actions)
        {
            actions.Opacity = 1;
        }
    }

    private void Row_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (HoverActions(sender) is { } actions && !InUse(actions))
        {
            actions.Opacity = 0;
        }
    }

    private void HoverActions_GotFocus(object sender, RoutedEventArgs e) => ((StackPanel)sender).Opacity = 1;

    private void HoverActions_LostFocus(object sender, RoutedEventArgs e)
    {
        var actions = (StackPanel)sender;
        if (!InUse(actions))
        {
            actions.Opacity = 0;
        }
    }

    /// <summary>Chọn đúng dòng đó trong ô chữ để sửa tay.</summary>
    private void EditLine_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row)
        {
            return;
        }
        var lines = PasteBox.Text.Replace("\r\n", "\r").Replace('\n', '\r').Split('\r'); // TextBox của WinUI dùng \r làm xuống dòng
        var index = row.Match.Line.Number - 1;
        if (index < lines.Length)
        {
            var start = lines.Take(index).Sum(l => l.Length + 1);
            PasteBox.Focus(FocusState.Programmatic);
            PasteBox.Select(start, lines[index].Length);
        }
    }

    /// <summary>Sửa cả dòng bằng popup giống form đơn. Áp dụng viết lại dòng chữ + ghim khách / món như đã chọn (đọc lại không
    /// bị đè); đơn chỉ vào dữ liệu khi bấm Lưu.</summary>
    private async void EditOrder_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row && await BatchOrderEditor.ShowAsync(XamlRoot, row.Match) is { } result)
        {
            ViewModel.RewriteLine(row, result.Line, result.Pin);
        }
    }

    /// <summary>Bảng tổng hợp so khớp (đọc lại trước để theo đúng ô chữ + dữ liệu mới nhất): các nhóm khác / chỉ có trong sổ /
    /// chỉ có trong MeRon / chưa so được / khớp; nút Chép kết quả.</summary>
    private async void Reconcile_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Refresh();
        var report = ViewModel.ReconcileReport();
        var panel = new StackPanel { Spacing = 4, MinWidth = 480 };
        if (report.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "Bảng nháp trống và ngày này chưa có đơn nào trong MeRon.", TextWrapping = TextWrapping.Wrap });
        }
        foreach (var (title, lines) in report)
        {
            panel.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 2) });
            foreach (var line in lines)
            {
                panel.Children.Add(new TextBlock { Text = "• " + line, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            }
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"So khớp sổ với MeRon - ngày giao {ViewModel.DeliveryDay:dd/MM/yyyy}",
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, // ContentDialog không tự cuộn
            PrimaryButtonText = "Chép kết quả",
            CloseButtonText = "Đóng",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var package = new DataPackage();
            package.SetText(ViewModel.ReconcileReportText());
            Clipboard.SetContent(package);
            ViewModel.Message = "Đã chép kết quả so khớp - dán vào Zalo / ghi chú bằng Ctrl+V.";
        }
    }

    private void CopyPrompt_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(ViewModel.ClaudePrompt());
        Clipboard.SetContent(package);
        ViewModel.Message = "Đã chép câu lệnh mẫu - mở Claude (claude.ai hoặc app điện thoại), dán câu lệnh (Ctrl+V), gửi kèm ảnh trang sổ, rồi chép kết quả dán vào ô chữ ở đây.";
    }

    /// <summary>Project "MeRon – đọc sổ đơn" trên claude.ai - đã có sẵn hướng dẫn chép sổ theo mẫu, chỉ cần gửi ảnh.</summary>
    private const string ClaudeProjectUrl = "https://claude.ai/project/01a1a082-e5ca-770f-921c-7a51f689414d";

    private void CopyClaudeLinkSplit_Click(SplitButton sender, SplitButtonClickEventArgs args) => CopyClaudeLink_Click(sender, new RoutedEventArgs());

    private void CopyClaudeLink_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(ClaudeProjectUrl);
        Clipboard.SetContent(package);
        ViewModel.Message = "Đã chép link Claude - mở trình duyệt, dán link (Ctrl+V), gửi ảnh trang sổ, rồi chép kết quả dán vào ô chữ ở đây.";
    }
}
