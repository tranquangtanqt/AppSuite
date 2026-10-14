using MeRon.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace MeRon.Views;

/// <summary>
/// Ô ngày thay CalendarDatePicker (không gõ được, lịch theo ngôn ngữ Windows): gõ thẳng dd/MM/yyyy (cả "14/10", "14", "141026" -
/// xem <see cref="DateInput"/>), Enter hoặc rời ô là nhận; gõ sai thì trả lại ngày cũ. ↑ / ↓ = ±1 ngày, Alt+↓ / F4 mở lịch.
/// Nút lịch: lịch tiếng Việt (tuần bắt đầu thứ Hai) + Hôm nay + Xoá ngày (khi <see cref="AllowEmpty"/>).
/// <see cref="Date"/> cùng kiểu với CalendarDatePicker.Date nên bind ViewModel như cũ.
/// </summary>
public sealed partial class DateBox : UserControl
{
    public static readonly DependencyProperty DateProperty = DependencyProperty.Register(
        nameof(Date), typeof(DateTimeOffset?), typeof(DateBox), new PropertyMetadata(null, (d, _) => ((DateBox)d).ShowDate()));

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(DateBox), new PropertyMetadata(null, (d, _) => ((DateBox)d).ShowHeader()));

    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText), typeof(string), typeof(DateBox), new PropertyMetadata(string.Empty, (d, e) => ((DateBox)d).Input.PlaceholderText = (string)e.NewValue));

    public static readonly DependencyProperty AllowEmptyProperty = DependencyProperty.Register(
        nameof(AllowEmpty), typeof(bool), typeof(DateBox), new PropertyMetadata(true, (d, e) => ((DateBox)d).ClearButton.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed));

    private bool _syncingCalendar;

    public DateBox()
    {
        InitializeComponent();
        PlaceholderText = "dd/mm/yyyy";
        // Tên cho trình đọc màn hình / UI Automation đặt trên DateBox → chuyển vào ô gõ (bỏ khỏi khung ngoài: không thì có 2 phần
        // tử cùng tên "Từ ngày"...).
        Loaded += (_, _) =>
        {
            var name = AutomationProperties.GetName(this);
            if (string.IsNullOrEmpty(name))
            {
                return;
            }
            AutomationProperties.SetName(Input, name);
            AutomationProperties.SetName(CalendarButton, $"Mở lịch {name}");
            AutomationProperties.SetName(this, string.Empty);
        };
    }

    public DateTimeOffset? Date
    {
        get => (DateTimeOffset?)GetValue(DateProperty);
        set => SetValue(DateProperty, value);
    }

    public string? Header
    {
        get => (string?)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>false: ngày bắt buộc - xoá trắng ô thì trả lại ngày cũ, không có nút Xoá ngày.</summary>
    public bool AllowEmpty
    {
        get => (bool)GetValue(AllowEmptyProperty);
        set => SetValue(AllowEmptyProperty, value);
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private DateOnly? Day => Date is { } d ? DateOnly.FromDateTime(d.Date) : null;

    private void SetDay(DateOnly? day)
    {
        if (day != Day)
        {
            Date = day is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue)) : null;
        }
        ShowDate();
    }

    private void ShowDate() => Input.Text = Day is { } d ? DateInput.Format(d) : string.Empty;

    private void ShowHeader()
    {
        HeaderText.Text = Header ?? string.Empty;
        HeaderText.Visibility = string.IsNullOrEmpty(Header) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Nhận chữ đang gõ: đúng ngày → đổi ngày; trống → bỏ ngày (nếu được); sai → trả lại ngày cũ.</summary>
    private void Commit()
    {
        if (string.IsNullOrWhiteSpace(Input.Text))
        {
            SetDay(AllowEmpty ? null : Day);
            return;
        }
        SetDay(DateInput.Parse(Input.Text, Today) ?? Day);
    }

    private void Input_LostFocus(object sender, RoutedEventArgs e) => Commit();

    private void Input_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var alt = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        switch (e.Key)
        {
            case VirtualKey.Enter:
                Commit();
                Input.SelectAll();
                e.Handled = true;
                break;
            case VirtualKey.Down when alt:
            case VirtualKey.F4:
                Commit();
                CalendarFlyout.ShowAt(CalendarButton);
                e.Handled = true;
                break;
            case VirtualKey.Up:
            case VirtualKey.Down:
                Commit();
                SetDay((Day ?? Today).AddDays(e.Key == VirtualKey.Up ? 1 : -1));
                Input.SelectAll();
                e.Handled = true;
                break;
        }
    }

    private void CalendarFlyout_Opening(object sender, object e)
    {
        Commit();
        TodayText.Text = $"Hôm nay ({Today:dd/MM})";
        _syncingCalendar = true;
        Calendar.SelectedDates.Clear();
        if (Date is { } date)
        {
            Calendar.SelectedDates.Add(date);
        }
        Calendar.SetDisplayDate(Date ?? DateTimeOffset.Now);
        _syncingCalendar = false;
    }

    private void Calendar_SelectedDatesChanged(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
    {
        if (_syncingCalendar || args.AddedDates.Count == 0)
        {
            return;
        }
        SetDay(DateOnly.FromDateTime(args.AddedDates[0].Date));
        CalendarFlyout.Hide();
        Input.Focus(FocusState.Programmatic);
    }

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        SetDay(Today);
        CalendarFlyout.Hide();
        Input.Focus(FocusState.Programmatic);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        SetDay(null);
        CalendarFlyout.Hide();
        Input.Focus(FocusState.Programmatic);
    }
}
