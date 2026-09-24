using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScreenCapture.Models;
using ScreenCapture.Services;
using ScreenCapture.Services.Interop;
using Windows.Graphics;
using WinRT.Interop;

namespace ScreenCapture.Views;

/// <summary>Cửa sổ Cài đặt (kiểu "Program Options" của PicPick). Chỉnh trên 1 bản sao của AppSettings;
/// bấm OK mới gọi <c>apply</c> (launcher lưu file + đăng ký lại phím tắt + áp cho Session/Editor).</summary>
public sealed partial class SettingsWindow : Window
{
    private static readonly (HotkeyAction Action, string Label)[] HotkeyRows =
    [
        (HotkeyAction.FullScreen, "Chụp toàn màn hình"),
        (HotkeyAction.ActiveWindow, "Chụp cửa sổ hiện tại"),
        (HotkeyAction.Region, "Chụp vùng chọn"),
        (HotkeyAction.FixedRegion, "Chụp vùng cố định"),
        (HotkeyAction.ScrollCapture, "Chụp cuộn trang"),
        (HotkeyAction.RepeatLast, "Chụp lại lần gần nhất"),
    ];

    private readonly IImageFileService _fileService;
    private readonly Func<AppSettings, IReadOnlyList<HotkeyBinding>> _apply;
    private readonly Dictionary<HotkeyAction, (CheckBox Shift, CheckBox Ctrl, CheckBox Alt, ComboBox Key, TextBlock Status)> _hotkeyRows = [];

    /// <param name="failedHotkeys">Phím tắt hiện đang đăng ký lỗi (hiện ⚠ ngay khi mở).</param>
    /// <param name="apply">Áp dụng + lưu settings; trả về các phím tắt đăng ký lỗi.</param>
    public SettingsWindow(AppSettings current, IEnumerable<HotkeyAction> failedHotkeys, IImageFileService fileService,
        Func<AppSettings, IReadOnlyList<HotkeyBinding>> apply)
    {
        InitializeComponent();
        _fileService = fileService;
        _apply = apply;

        var scale = NativeMethods.GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(860 * scale), (int)(600 * scale)));

        // NumberBox: Minimum/Maximum gán qua code, không qua XAML (bài học XamlParseException của Slider).
        DelayBox.Minimum = 0; DelayBox.Maximum = 10; DelayBox.SmallChange = 1;
        MaxTabsBox.Minimum = 1; MaxTabsBox.Maximum = 100; MaxTabsBox.SmallChange = 1;
        MaxMegabytesBox.Minimum = 50; MaxMegabytesBox.Maximum = 5000; MaxMegabytesBox.SmallChange = 50;

        BuildHotkeyGrid();
        FillFrom(current);
        ShowHotkeyStatus(failedHotkeys);
        SessionFolderText.Text = $"{SessionService.Folder}  (đang dùng {SessionService.CurrentSizeBytes() / 1024.0 / 1024.0:0.#} MB)";
        CategoryList.SelectedIndex = 0;
    }

    private void BuildHotkeyGrid()
    {
        HotkeyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 3; i++)
        {
            HotkeyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }
        HotkeyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        HotkeyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

        for (int row = 0; row < HotkeyRows.Length; row++)
        {
            var (action, label) = HotkeyRows[row];
            HotkeyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            var shift = new CheckBox { Content = "Shift", MinWidth = 0 };
            var ctrl = new CheckBox { Content = "Ctrl", MinWidth = 0 };
            var alt = new CheckBox { Content = "Alt", MinWidth = 0 };
            var key = new ComboBox { ItemsSource = HotkeyBinding.AvailableKeys, HorizontalAlignment = HorizontalAlignment.Stretch };
            var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"] };

            UIElement[] cells = [text, shift, ctrl, alt, key, status];
            for (int col = 0; col < cells.Length; col++)
            {
                Grid.SetRow((FrameworkElement)cells[col], row);
                Grid.SetColumn((FrameworkElement)cells[col], col);
                HotkeyGrid.Children.Add(cells[col]);
            }
            _hotkeyRows[action] = (shift, ctrl, alt, key, status);
        }
    }

    private void FillFrom(AppSettings s)
    {
        DelayBox.Value = s.CaptureDelaySeconds;
        CopyAfterCaptureBox.IsChecked = s.CopyToClipboardAfterCapture;
        RunInTrayBox.IsChecked = s.RunInTray;
        StartWithWindowsBox.IsChecked = s.StartWithWindows;
        AutoSaveBox.IsChecked = s.AutoSave;
        AutoSaveFolderBox.Text = s.AutoSaveFolder;
        AutoSaveFolderPanel.Opacity = s.AutoSave ? 1 : 0.5;
        RememberTabsBox.IsChecked = s.RememberTabs;
        MaxTabsBox.Value = s.SessionMaxTabs;
        MaxMegabytesBox.Value = s.SessionMaxMegabytes;
        foreach (var (action, row) in _hotkeyRows)
        {
            var binding = s.GetHotkey(action);
            row.Shift.IsChecked = binding.Shift;
            row.Ctrl.IsChecked = binding.Ctrl;
            row.Alt.IsChecked = binding.Alt;
            row.Key.SelectedItem = HotkeyBinding.AvailableKeys.Contains(binding.Key) ? binding.Key : HotkeyBinding.NoKey;
        }
    }

    private AppSettings ReadSettings()
    {
        static int Int(NumberBox box, int fallback) => double.IsNaN(box.Value) ? fallback : (int)box.Value;

        var s = new AppSettings
        {
            CaptureDelaySeconds = Int(DelayBox, 0),
            CopyToClipboardAfterCapture = CopyAfterCaptureBox.IsChecked == true,
            RunInTray = RunInTrayBox.IsChecked == true,
            StartWithWindows = StartWithWindowsBox.IsChecked == true,
            AutoSave = AutoSaveBox.IsChecked == true,
            AutoSaveFolder = AutoSaveFolderBox.Text.Trim(),
            RememberTabs = RememberTabsBox.IsChecked == true,
            SessionMaxTabs = Int(MaxTabsBox, 30),
            SessionMaxMegabytes = Int(MaxMegabytesBox, 300),
            Hotkeys = [],
        };
        foreach (var (action, row) in _hotkeyRows)
        {
            s.Hotkeys.Add(new HotkeyBinding
            {
                Action = action,
                Shift = row.Shift.IsChecked == true,
                Ctrl = row.Ctrl.IsChecked == true,
                Alt = row.Alt.IsChecked == true,
                Key = row.Key.SelectedItem as string ?? HotkeyBinding.NoKey,
            });
        }
        return s;
    }

    private void ShowHotkeyStatus(IEnumerable<HotkeyAction> failed)
    {
        var failedSet = failed.ToHashSet();
        foreach (var (action, row) in _hotkeyRows)
        {
            bool isFailed = failedSet.Contains(action);
            row.Status.Text = isFailed ? "⚠" : string.Empty;
            ToolTipService.SetToolTip(row.Status, isFailed ? "Tổ hợp phím này đang bị Windows hoặc app khác giữ - chọn tổ hợp khác." : null);
        }
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var tag = (CategoryList.SelectedItem as ListViewItem)?.Tag as string;
        GeneralPage.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
        AutoSavePage.Visibility = tag == "AutoSave" ? Visibility.Visible : Visibility.Collapsed;
        SessionPage.Visibility = tag == "Session" ? Visibility.Visible : Visibility.Collapsed;
        HotkeysPage.Visibility = tag == "Hotkeys" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AutoSaveBox_Changed(object sender, RoutedEventArgs e) =>
        AutoSaveFolderPanel.Opacity = AutoSaveBox.IsChecked == true ? 1 : 0.5;

    private async void BrowseAutoSaveFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = await _fileService.PickFolderAsync(WindowNative.GetWindowHandle(this));
        if (folder is not null)
        {
            AutoSaveFolderBox.Text = folder;
        }
    }

    private void OpenSessionFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SessionService.Folder);
        System.Diagnostics.Process.Start("explorer.exe", SessionService.Folder);
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        FillFrom(new AppSettings());
        ValidationText.Text = "Đã đưa về mặc định - bấm OK để lưu.";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = ReadSettings();

        if (settings.AutoSave && string.IsNullOrWhiteSpace(settings.AutoSaveFolder))
        {
            ValidationText.Text = "Chọn thư mục cho Tự động lưu.";
            CategoryList.SelectedIndex = 1;
            return;
        }

        // 2 thao tác không được dùng chung 1 tổ hợp phím.
        var duplicate = settings.Hotkeys.Where(h => h.IsEnabled).GroupBy(h => h.Describe()).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            ValidationText.Text = $"Tổ hợp {duplicate.Key} đang dùng cho nhiều thao tác.";
            CategoryList.SelectedIndex = 3;
            return;
        }

        var failed = _apply(settings);
        if (failed.Count > 0)
        {
            // Đã lưu, nhưng giữ cửa sổ mở để người dùng đổi các phím bị chiếm.
            ShowHotkeyStatus(failed.Select(f => f.Action));
            ValidationText.Text = "Đã lưu. Phím tắt có ⚠ đang bị chiếm, hãy chọn tổ hợp khác (hoặc bấm Huỷ để đóng).";
            CategoryList.SelectedIndex = 3;
            return;
        }
        Close();
    }
}
