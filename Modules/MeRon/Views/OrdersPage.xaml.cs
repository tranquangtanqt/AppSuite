using MeRon.Core;
using MeRon.Core.Models;
using MeRon.Core.Orders;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MeRon.Views;

/// <summary>
/// Danh sách đơn. Bộ lọc giữ trong 1 ViewModel dùng chung cả phiên: quay lại từ form / Chi tiết (Back) giữ nguyên bộ lọc;
/// vào lại từ menu cũng giữ (mặc định chỉ ở lần đầu mở trong phiên). Trang KHÔNG cache: SelectorBar trong trang cache tự áp lại
/// tab cũ khi gắn lại vào cửa sổ, lúc có lúc không (quay lại từ Chi tiết thì bộ lọc tự về "Tất cả").
/// </summary>
public sealed partial class OrdersPage : Page, IOrderListPage
{
    private static readonly OrdersViewModel Session = new();

    public OrdersViewModel ViewModel => Session;

    public OrdersPage()
    {
        InitializeComponent();
        CustomerSuggest.Attach(CustomerBox, () => ViewModel.Customers, () => ViewModel.Customer, c => ViewModel.Customer = c);
        Shortcuts.Search(this, CustomerBox);
        Shortcuts.StatusTabs(this, StatusBar);
        Shortcuts.Ctrl(this, Windows.System.VirtualKey.D, () => RangeMenu.ShowAt(RangeButton));
        foreach (var sort in Enum.GetValues<OrderSort>())
        {
            var item = new RadioMenuFlyoutItem { Text = OrdersViewModel.SortLabel(sort), Tag = sort, GroupName = "sort" };
            item.Click += (_, _) => ViewModel.Sort = sort;
            SortMenu.Items.Add(item);
        }
        foreach (var range in Enum.GetValues<DateRangePreset>())
        {
            var item = new MenuFlyoutItem { Text = OrdersViewModel.RangeLabel(range) };
            item.Click += (_, _) => ViewModel.ApplyRange(range);
            RangeMenu.Items.Add(item);
        }
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OrdersViewModel.Sort))
        {
            ShowSort();
        }
    }

    private static bool _filterInitialized;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel.PropertyChanged += OnViewModelChanged;
        // Bộ lọc mặc định (Đã lên đơn, hôm nay) chỉ ở lần đầu mở trang trong phiên; vào lại từ menu thì giữ điều kiện đã chọn.
        if (!_filterInitialized)
        {
            ViewModel.Reset();
            _filterInitialized = true;
        }
        ViewModel.Load();
        CustomerBox.Text = ViewModel.Customer?.Name ?? string.Empty;
        SyncStatusBar();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, SyncStatusBar);
        ShowSort();
        RefreshUnsynced();
    }

    // ---------- Nhắc xuất: thay đổi trên máy tính chưa xuất sang điện thoại ----------

    /// <summary>Số dòng lúc bấm ✕ - còn đúng số đó thì không nhắc lại (trong phiên); có thêm thay đổi thì nhắc lại.</summary>
    private static long _dismissedCount = -1;

    private void RefreshUnsynced()
    {
        var edits = Services.AppServices.Database.GetUnsyncedEdits();
        UnsyncedBar.Severity = InfoBarSeverity.Warning;
        UnsyncedBar.Title = "Chưa xuất sang điện thoại";
        UnsyncedBar.Message = edits.Describe() + " Lần nhập backup từ điện thoại sau sẽ đè mất - nên xuất trước.";
        UploadDriveButton.IsEnabled = true;
        UploadDriveButton.Content = string.IsNullOrEmpty(Services.GoogleServices.Auth?.Email)
            ? "Xuất lên Google Drive (đăng nhập ở trang Xuất)"
            : "Xuất lên Google Drive";
        UnsyncedBar.IsOpen = edits.Any && edits.Count != _dismissedCount;
    }

    private void UnsyncedBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (args.Reason == InfoBarCloseReason.CloseButton)
        {
            _dismissedCount = Services.AppServices.Database.GetUnsyncedEdits().Count;
        }
    }

    /// <summary>Đã đăng nhập Google (trang Xuất) → xuất + tải lên Drive ngay tại đây (bộ đếm về 0); chưa → mở trang Xuất.</summary>
    private async void UploadDrive_Click(object sender, RoutedEventArgs e)
    {
        var export = new ExportViewModel();
        export.RefreshGoogle();
        if (!export.IsSignedIn)
        {
            App.MainWindow?.SelectMenu("ExportPage");
            return;
        }
        UploadDriveButton.IsEnabled = false;
        UnsyncedBar.Message = "Đang xuất và tải lên Google Drive...";
        await export.UploadToDriveAsync();
        var left = Services.AppServices.Database.GetUnsyncedEdits();
        UnsyncedBar.Severity = left.Any ? InfoBarSeverity.Error : InfoBarSeverity.Success;
        UnsyncedBar.Title = left.Any ? "Chưa xuất được" : "Đã xuất";
        UnsyncedBar.Message = export.DriveStatus + (left.Any ? string.Empty : " Nhập bản này vào điện thoại để điện thoại có các đơn mới.");
        UploadDriveButton.IsEnabled = left.Any;
    }

    private void OpenExport_Click(object sender, RoutedEventArgs e) => App.MainWindow?.SelectMenu("ExportPage");

    /// <summary>
    /// ViewModel sống cả phiên, trang thì không - gỡ mọi chỗ nghe ViewModel (x:Bind, danh sách thẻ): trang cũ đã gỡ khỏi cửa sổ
    /// mà còn nghe Cards thì ném COMException "Unspecified error" khi danh sách đổi.
    /// </summary>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelChanged;
        Bindings.StopTracking();
        CardsRepeater.ItemsSource = null;
    }

    private bool _syncing;

    private void SyncStatusBar()
    {
        _syncing = true;
        var tag = ViewModel.Status is { } s ? ((int)s).ToString() : "0";
        StatusBar.SelectedItem = StatusBar.Items.First(i => (string)i.Tag == tag);
        _syncing = false;
    }

    /// <summary>Nút Sắp xếp hiện cách đang chọn (đậm khi khác mặc định, như icon đổi màu của điện thoại).</summary>
    private void ShowSort()
    {
        foreach (var item in SortMenu.Items.OfType<RadioMenuFlyoutItem>())
        {
            item.IsChecked = (OrderSort)item.Tag == ViewModel.Sort;
        }
        SortText.Text = ViewModel.Sort == OrderSort.Default ? "Sắp xếp" : OrdersViewModel.SortLabel(ViewModel.Sort);
        SortText.FontWeight = ViewModel.Sort == OrderSort.Default ? Microsoft.UI.Text.FontWeights.Normal : Microsoft.UI.Text.FontWeights.SemiBold;
    }

    private void StatusBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        // Bỏ qua thay đổi do chính trang đặt, hoặc SelectorBar tự áp lại lúc trang chưa gắn vào cửa sổ.
        if (_syncing || !IsLoaded)
        {
            return;
        }
        ViewModel.Status = sender.SelectedItem?.Tag is string tag && tag != "0" ? (OrderStatus)int.Parse(tag) : null;
    }

    private void ClearCustomer_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Customer = null;
        CustomerBox.Text = string.Empty;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => ViewModel.Load();

    public void ReloadOrders() => ViewModel.Load();

    private void Add_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(OrderFormPage), ViewModel.NewOrderArgs());

    private void Detail_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(OrderDetailPage), new OrderDetailArgs(ViewModel.Filter));

    /// <summary>Bấm thẻ: mở đơn; chế độ Chọn nhiều thì đổi tick (Shift + bấm: chọn cả dải từ thẻ bấm trước).</summary>
    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: OrderCardViewModel card })
        {
            return;
        }
        if (ViewModel.SelectMode)
        {
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            ViewModel.Toggle(card, shift);
            return;
        }
        Frame.Navigate(typeof(OrderFormPage), new OrderFormArgs(card.Id));
    }

    // ---------- Chọn nhiều: sửa hàng loạt (BulkEdit, BulkEditDialog) ----------

    public static bool Positive(int count) => count > 0;

    public static string SelectName(string accessibleName) => "Chọn " + accessibleName;

    /// <summary>Ô Chọn hết: CheckBox tự đổi khi bấm - đặt lại theo ViewModel (giá trị không đổi thì x:Bind không đặt lại).</summary>
    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleAll();
        SelectAllBox.IsChecked = ViewModel.AllSelected;
    }

    private void CloseSelect_Click(object sender, RoutedEventArgs e) => ViewModel.SelectMode = false;

    private void BulkBar_Closed(InfoBar sender, InfoBarClosedEventArgs args) => ViewModel.BulkMessage = string.Empty;

    /// <summary>Hộp xem trước → ghi; lỗi thì báo.</summary>
    private async Task RunBulk(string title, UIElement? input, Func<IReadOnlyList<Order>, (BulkPlan? Plan, string? Hint)> build,
        Action<Action>? watch = null, bool delete = false)
    {
        var orders = ViewModel.SelectedOrders();
        if (orders.Count == 0)
        {
            ViewModel.Load();
            return;
        }
        if (await BulkEditDialog.ShowAsync(XamlRoot, $"{title} - {orders.Count} đơn đã chọn", input, () => build(orders), watch, delete) is { } plan
            && ViewModel.ApplyBulk(plan) is { } error)
        {
            await Dialogs.ShowMessageAsync(XamlRoot, $"{title} không được", error);
        }
    }

    private async void BulkDelivery_Click(object sender, RoutedEventArgs e)
    {
        var (box, watch) = BulkEditDialog.DateInput("Ngày giao mới", DateOnly.FromDateTime(DateTime.Today));
        await RunBulk("Đổi ngày giao", box, orders => BulkEditDialog.DayOf(box) is { } day
            ? (BulkEdit.DeliveryDay(orders, day), null) : (null, "Chọn ngày giao."), watch);
    }

    private async void BulkPayment_Click(object sender, RoutedEventArgs e)
    {
        var (box, watch) = BulkEditDialog.DateInput("Ngày thanh toán", DateOnly.FromDateTime(DateTime.Today));
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(box);
        panel.Children.Add(new TextBlock
        {
            Text = "Đơn đã thanh toán: đổi sang ngày này (giữ giờ). Đơn chưa thanh toán: chuyển sang Đã thanh toán ngày này. Đơn huỷ bỏ qua.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
        });
        await RunBulk("Ngày thanh toán", panel, orders => BulkEditDialog.DayOf(box) is { } day
            ? (BulkEdit.Payment(orders, day, TimeOnly.FromDateTime(DateTime.Now)), null) : (null, "Chọn ngày thanh toán."), watch);
    }

    private async void BulkUnpay_Click(object sender, RoutedEventArgs e) =>
        await RunBulk("Trả về Đã lên đơn", null, orders => (BulkEdit.Unpay(orders), null));

    private async void BulkCancel_Click(object sender, RoutedEventArgs e) =>
        await RunBulk("Huỷ đơn", null, orders => (BulkEdit.Cancel(orders), null));

    private async void BulkRestore_Click(object sender, RoutedEventArgs e) =>
        await RunBulk("Khôi phục đơn huỷ", null, orders => (BulkEdit.Restore(orders), null));

    private async void BulkDelete_Click(object sender, RoutedEventArgs e) =>
        await RunBulk("Xoá đơn", null, orders => (BulkEdit.Delete(orders), null), delete: true);

    /// <summary>Tên món: chọn 1 món có trong các đơn đã chọn → Đổi tên (giữ giá) / Thay bằng món khác; hoặc Cập nhật mọi tên theo Danh mục.</summary>
    private async void BulkItems_Click(object sender, RoutedEventArgs e)
    {
        var orders = ViewModel.SelectedOrders();
        var products = Services.AppServices.MasterData.GetProducts();
        var productMap = products.ToDictionary(p => p.Id);
        var names = BulkEdit.ItemNames(orders, productMap);
        var sorted = products.Where(p => !string.IsNullOrWhiteSpace(p.Name)).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        var mode = new RadioButtons { Header = "Cách đổi", MaxColumns = 1 };
        mode.Items.Add("Đổi tên món (giữ giá)");
        mode.Items.Add("Thay bằng món khác trong danh sách");
        mode.Items.Add("Cập nhật mọi tên món theo Danh mục (đơn ghi tên cũ)");
        mode.SelectedIndex = names.Any(n => n.OutdatedName) ? 2 : 0;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(mode, "Cách đổi");
        var item = new ComboBox { Header = "Món trên đơn", ItemsSource = names.Select(n => n.Label).ToList(), MinWidth = 360, SelectedIndex = names.Count > 0 ? 0 : -1 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, "Món trên đơn");
        var newName = new TextBox { Header = "Tên mới", MinWidth = 360, HorizontalAlignment = HorizontalAlignment.Left };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(newName, "Tên mới");
        var replacement = new ComboBox { Header = "Thay bằng món", ItemsSource = sorted.Select(p => p.Name).ToList(), MinWidth = 360, PlaceholderText = "Chọn món (gõ chữ đầu để nhảy tới)" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(replacement, "Thay bằng món");
        var note = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };

        void ShowInputs()
        {
            item.Visibility = mode.SelectedIndex == 2 ? Visibility.Collapsed : Visibility.Visible;
            newName.Visibility = mode.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            replacement.Visibility = mode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            note.Text = mode.SelectedIndex switch
            {
                1 => "Đơn chưa thanh toán / huỷ: lấy giá món mới, tính lại tổng. Đơn đã thanh toán: giữ giá và tiền đã thu, chỉ đổi món + tên.",
                2 => "Đơn ghi tên món lúc đặt; món đã đổi tên trong Danh mục thì đơn cũ vẫn ghi tên cũ. Giữ giá.",
                _ when Target() is { IsFree: false } => "Món trong Danh mục: đổi tên luôn trong Danh mục (đơn mới, form đơn dùng tên mới) và trên các đơn "
                    + "đã chọn có món này; giá giữ nguyên. Đơn không chọn vẫn ghi tên cũ - đổi sau bằng Cập nhật mọi tên món theo Danh mục.",
                _ => "Món tự do: chỉ đổi chữ ghi trên đơn (vd gõ sai), giá và số lượng giữ nguyên.",
            };
        }
        BulkItemName? Target() => item.SelectedIndex >= 0 && item.SelectedIndex < names.Count ? names[item.SelectedIndex] : null;
        item.SelectionChanged += (_, _) =>
        {
            newName.Text = Target() is { } t ? t.CatalogName ?? t.Name : string.Empty;
            ShowInputs();
        };
        newName.Text = Target() is { } first ? first.CatalogName ?? first.Name : string.Empty;
        ShowInputs();

        var panel = new StackPanel { Spacing = 8 };
        foreach (var element in new UIElement[] { mode, item, newName, replacement, note })
        {
            panel.Children.Add(element);
        }

        await RunBulk("Tên món", panel, selected =>
        {
            switch (mode.SelectedIndex)
            {
                case 2:
                    return (BulkEdit.SyncCatalogNames(selected, productMap), null);
                case 1:
                    if (Target() is not { } t1)
                    {
                        return (null, "Chọn món trên đơn.");
                    }
                    return replacement.SelectedIndex >= 0 ? (BulkEdit.ReplaceItem(selected, t1, sorted[replacement.SelectedIndex]), null) : (null, "Chọn món thay vào.");
                default:
                    if (Target() is not { } t0)
                    {
                        return (null, "Chọn món trên đơn.");
                    }
                    if (!t0.IsFree)
                    {
                        return BulkEdit.RenameProduct(selected, t0, newName.Text, productMap);
                    }
                    return string.IsNullOrWhiteSpace(newName.Text) ? (null, "Gõ tên mới.")
                        : newName.Text.Trim() == t0.Name ? (null, "Tên mới giống tên đang ghi.")
                        : (BulkEdit.RenameItem(selected, t0, newName.Text), null);
            }
        }, refresh =>
        {
            mode.SelectionChanged += (_, _) => { ShowInputs(); refresh(); };
            item.SelectionChanged += (_, _) => refresh();
            newName.TextChanged += (_, _) => refresh();
            replacement.SelectionChanged += (_, _) => refresh();
        });
    }

    /// <summary>Hoàn tác lô vừa sửa: chỉ đơn còn y như lúc vừa sửa; đơn đã sửa / xoá sau đó giữ nguyên.</summary>
    private async void BulkUndo_Click(object sender, RoutedEventArgs e)
    {
        var (revert, keep, rename) = ViewModel.PlanUndoBulk();
        var kept = keep.Count == 0 ? string.Empty : $"\n\nGiữ nguyên {keep.Count} đơn đã sửa / xoá sau đó.";
        if (revert.Count == 0 && rename is null)
        {
            await Dialogs.ShowMessageAsync(XamlRoot, "Hoàn tác sửa hàng loạt", "Không còn đơn nào trả lại được." + kept);
            return;
        }
        var question = (revert.Count > 0 ? $"Trả {revert.Count} đơn về như trước khi sửa?" : "Không còn đơn nào trả lại được.")
                       + (rename is null ? string.Empty : $"\n\nDanh mục: trả tên \"{rename.NewName}\" về \"{rename.OldName}\".");
        if (await Dialogs.ConfirmAsync(XamlRoot, "Hoàn tác sửa hàng loạt", question + kept, revert.Count > 0 ? $"Hoàn tác {revert.Count} đơn" : "Hoàn tác")
            && ViewModel.UndoBulk(revert, keep.Count, rename) is { } error)
        {
            await Dialogs.ShowMessageAsync(XamlRoot, "Hoàn tác không được", error);
        }
    }
}
