using DbTools.Core;
using DbTools.Models;
using DbTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DbTools.Views;

/// <summary>
/// Ô chọn server (ComboBox) ở các trang: <c>views:ProfilePicker.Selected="{x:Bind ..., Mode=TwoWay}"</c> thay cho ItemsSource +
/// SelectedItem. Danh sách xếp theo cây thư mục, dựng lại mỗi lần ô hiện ra (hồ sơ vừa thêm / xoá / chuyển thư mục). Dựng lại
/// danh sách làm ComboBox bỏ chọn trong chốc lát: bỏ qua lần đó, hồ sơ đang chọn của trang giữ nguyên (gắn thẳng SelectedItem
/// thì trang mất hồ sơ, có thể sang DB khác mà người dùng không biết).
/// </summary>
public static class ProfilePicker
{
    public static readonly DependencyProperty SelectedProperty = DependencyProperty.RegisterAttached(
        "Selected", typeof(ConnectionProfile), typeof(ProfilePicker), new PropertyMetadata(null, OnSelectedChanged));

    public static ConnectionProfile? GetSelected(DependencyObject box) => (ConnectionProfile?)box.GetValue(SelectedProperty);

    public static void SetSelected(DependencyObject box, ConnectionProfile? value) => box.SetValue(SelectedProperty, value);

    private static readonly DependencyProperty HookedProperty = DependencyProperty.RegisterAttached(
        "Hooked", typeof(bool), typeof(ProfilePicker), new PropertyMetadata(false));

    private static void OnSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (ComboBox)d;
        if (!(bool)box.GetValue(HookedProperty))
        {
            box.SetValue(HookedProperty, true);
            box.Loaded += (_, _) => Show(box);
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedItem is ConnectionProfile picked && picked != GetSelected(box))
                {
                    SetSelected(box, picked);
                }
            };
        }
        Show(box);
    }

    private static void Show(ComboBox box)
    {
        var profiles = ProfileTree.Flatten(AppState.Profiles);
        if (box.ItemsSource is not List<ConnectionProfile> current || !current.SequenceEqual(profiles))
        {
            box.ItemsSource = profiles;
        }
        box.SelectedItem = GetSelected(box);
    }
}
