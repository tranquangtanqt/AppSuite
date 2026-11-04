using CsvEditor.Models;
using CsvEditor.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VirtualKey = Windows.System.VirtualKey;
using Windows.UI.Core;

namespace CsvEditor.Views;

/// <summary>Thanh Tìm / Thay thế (Ctrl+F / Ctrl+H) phía trên bảng, thay cho hộp thoại che bảng cũ. Kết quả nằm ở
/// ViewModel và tự tính lại khi bảng đổi (<see cref="CsvEditorViewModel.Find"/>) vì bảng vẫn sửa / lọc được lúc thanh đang mở.</summary>
public sealed partial class FindBar : UserControl
{
    private readonly DispatcherQueueTimer _debounceTimer;

    public CsvEditorViewModel ViewModel { get; set; } = null!;

    /// <summary>Tới kết quả mới: MainWindow chọn dòng và cuộn tới đúng ô.</summary>
    public event EventHandler<CellRef>? MatchNavigated;

    /// <summary>Thanh vừa đóng: MainWindow trả con trỏ về bảng.</summary>
    public event EventHandler? CloseRequested;

    public FindBar()
    {
        InitializeComponent();

        _debounceTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _debounceTimer.Interval = TimeSpan.FromMilliseconds(300);
        _debounceTimer.IsRepeating = false;
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            RunFind();
        };

        ModeComboBox.SelectedIndex = 0;
    }

    /// <summary>Mở thanh và đưa con trỏ vào ô Tìm (chọn sẵn chữ cũ để gõ đè). <paramref name="replace"/>: hiện cả ô Thay bằng.</summary>
    public void Open(bool replace)
    {
        var wasOpen = Visibility == Visibility.Visible;
        Visibility = Visibility.Visible;
        if (replace)
        {
            SetReplaceVisible(true);
        }

        if (!wasOpen)
        {
            RunFind();
        }

        // Thanh vừa hiện chưa được xếp chỗ, Focus() ngay lúc này không có tác dụng.
        DispatcherQueue.TryEnqueue(() =>
        {
            FindTextBox.Focus(FocusState.Keyboard);
            FindTextBox.SelectAll();
        });
    }

    private void Close()
    {
        _debounceTimer.Stop();
        ViewModel.ClearFind();
        Visibility = Visibility.Collapsed;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetReplaceVisible(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ReplaceToggle.IsChecked = visible;
        ReplaceToggleIcon.Glyph = visible ? "" : "";
        ReplaceTextBox.Visibility = visibility;
        ReplaceAllButton.Visibility = visibility;
        ReplaceStatusText.Visibility = visibility;
    }

    private void RunFind()
    {
        // Thanh còn đóng: ModeComboBox.SelectedIndex = 0 lúc khởi tạo chạy vào đây khi chưa có ViewModel.
        if (ViewModel is null || Visibility != Visibility.Visible)
        {
            return;
        }

        ReplaceStatusText.Text = string.Empty;
        ViewModel.Find(FindTextBox.Text, (SearchMode)ModeComboBox.SelectedIndex, CaseSensitiveToggle.IsChecked == true);
        if (ViewModel.CurrentMatch is { } match)
        {
            MatchNavigated?.Invoke(this, match);
        }
    }

    private void Go(CellRef? match)
    {
        if (match is { } cell)
        {
            MatchNavigated?.Invoke(this, cell);
        }
    }

    private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void FindTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        if (_debounceTimer.IsRunning)
        {
            // Vừa gõ xong, chưa kịp tìm: Enter = tìm luôn, không nhảy qua kết quả đầu.
            _debounceTimer.Stop();
            RunFind();
            return;
        }

        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        Go(shift ? ViewModel.PreviousMatch() : ViewModel.NextMatch());
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => RunFind();

    private void CaseSensitiveToggle_Click(object sender, RoutedEventArgs e) => RunFind();

    private void ReplaceToggle_Click(object sender, RoutedEventArgs e) => SetReplaceVisible(ReplaceToggle.IsChecked == true);

    private void PreviousButton_Click(object sender, RoutedEventArgs e) => Go(ViewModel.PreviousMatch());

    private void NextButton_Click(object sender, RoutedEventArgs e) => Go(ViewModel.NextMatch());

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ReplaceAllButton_Click(object sender, RoutedEventArgs e)
    {
        var count = ViewModel.ReplaceAllMatches(ReplaceTextBox.Text);
        ReplaceStatusText.Text = $"Đã thay {count} ô.";
    }
}
