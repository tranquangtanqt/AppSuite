using DbTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DbTools.Views;

public sealed partial class ServerLogPage : Page
{
    public ServerLogViewModel ViewModel { get; } = new();

    /// <summary>Trước khi Theo dõi thêm dòng: người dùng đang ở cuối bảng không (đang cuộn lên xem dòng cũ thì không kéo xuống).</summary>
    private bool _atBottom = true;

    public ServerLogPage()
    {
        InitializeComponent();
        foreach (var label in ServerLogViewModel.ReadSizeLabels)
        {
            SizeBox.Items.Add(label);
        }
        SizeBox.SelectedIndex = ViewModel.ReadSizeIndex;
        // "Cuối" = phía dòng mới nhất: cuối bảng khi xếp cũ trước, đầu bảng khi xếp mới trước.
        ViewModel.RowsAppending += (_, _) => _atBottom = ListScroll() is not { } sv
            || (ViewModel.NewestFirst ? sv.VerticalOffset <= 40 : sv.VerticalOffset >= sv.ScrollableHeight - 40);
        ViewModel.RowsAppended += (_, _) =>
        {
            if (_atBottom)
            {
                ScrollToNewest();
            }
        };
        ViewModel.ScrollToEndRequested += (_, _) =>
        {
            _atBottom = true;
            ScrollToNewest();
        };
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.EnsureProfile();
    }

    /// <summary>Rời trang thì thôi theo dõi (không giữ kết nối tới server khi không xem).</summary>
    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.StopFollow();
    }

    private void ScrollToNewest()
    {
        if (ViewModel.Rows.Count > 0)
        {
            LogList.ScrollIntoView(ViewModel.NewestFirst ? ViewModel.Rows[0] : ViewModel.Rows[^1]);
        }
    }

    private ScrollViewer? ListScroll() => FindChild<ScrollViewer>(LogList);

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found)
            {
                return found;
            }
            if (FindChild<T>(child) is { } deeper)
            {
                return deeper;
            }
        }
        return null;
    }
}
