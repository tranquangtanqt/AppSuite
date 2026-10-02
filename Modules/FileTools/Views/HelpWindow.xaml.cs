using System.Runtime.InteropServices;
using FileTools.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace FileTools.Views;

/// <summary>Cửa sổ Hướng dẫn (mục cuối menu / nút ? / F1): danh mục bên trái, nội dung bên phải, ô tìm kiếm không phân
/// biệt hoa thường / dấu. Bố cục chép từ ScreenCapture.Views.HelpWindow (module không reference nhau); nội dung ở
/// <see cref="HelpContent"/>.</summary>
public sealed partial class HelpWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    public HelpWindow(string? pageTag)
    {
        InitializeComponent();
        SharedUI.Helpers.WindowIcon.Apply(this);

        // 1000×700 logic nhưng không vượt 90% vùng làm việc (màn nhỏ ở 150% chỉ cao ~600 logic), căn giữa.
        var scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int width = (int)Math.Min(1000 * scale, work.Width * 0.9);
        int height = (int)Math.Min(700 * scale, work.Height * 0.9);
        AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));

        foreach (var section in HelpContent.Sections)
        {
            SectionList.Items.Add(new ListViewItem
            {
                Tag = section,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children =
                    {
                        new FontIcon { Glyph = section.Glyph, FontSize = 14 },
                        new TextBlock { Text = section.Title, TextTrimming = TextTrimming.CharacterEllipsis },
                    },
                },
            });
        }
        ShowPage(pageTag);
    }

    /// <summary>Mở mục của 1 trang (F1 khi cửa sổ đã mở sẵn); xoá ô tìm kiếm.</summary>
    public void ShowPage(string? pageTag)
    {
        var target = HelpContent.ForPage(pageTag);
        SearchBox.Text = string.Empty;
        var item = SectionList.Items.OfType<ListViewItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, target));
        SectionList.SelectedItem = item;
        // Cuộn danh mục tới mục đang chọn sau khi danh sách đã bố cục (gọi ngay trong constructor thì không có tác dụng).
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => SectionList.ScrollIntoView(item));
    }

    private void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SectionList.SelectedItem is not null && SearchBox.Text.Length > 0)
        {
            SearchBox.Text = string.Empty; // chọn danh mục = thoát tìm kiếm (TextChanged sẽ Render)
            return;
        }
        Render();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchBox.Text.Trim().Length > 0)
        {
            SectionList.SelectedItem = null;
        }
        else if (SectionList.SelectedItem is null)
        {
            SectionList.SelectedIndex = 0;
        }
        Render();
    }

    private void Render()
    {
        ContentPanel.Children.Clear();
        ContentScroller.ChangeView(null, 0, null, true);

        string query = SearchBox.Text.Trim();
        if (query.Length == 0)
        {
            if (SectionList.SelectedItem is ListViewItem { Tag: HelpSection section })
            {
                AddSectionHeader(section, large: true);
                foreach (var item in section.Items)
                {
                    ContentPanel.Children.Add(BuildItem(item));
                }
            }
            return;
        }

        var results = HelpContent.Search(query);
        int count = results.Sum(r => r.Items.Count);
        ContentPanel.Children.Add(new TextBlock
        {
            Text = count == 0 ? $"Không tìm thấy mục nào khớp \"{query}\"." : $"{count} kết quả cho \"{query}\"",
            Foreground = SecondaryBrush,
            Margin = new Thickness(0, 0, 0, 4),
        });
        foreach (var (section, items) in results)
        {
            AddSectionHeader(section, large: false);
            foreach (var item in items)
            {
                ContentPanel.Children.Add(BuildItem(item));
            }
        }
    }

    private void AddSectionHeader(HelpSection section, bool large)
    {
        ContentPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, large || ContentPanel.Children.Count <= 1 ? 0 : 14, 0, 0),
            Children =
            {
                new FontIcon { Glyph = section.Glyph, FontSize = large ? 20 : 16, Foreground = AccentBrush },
                new TextBlock { Text = section.Title, FontSize = large ? 20 : 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center },
            },
        });
        if (large)
        {
            ContentPanel.Children.Add(new TextBlock
            {
                Text = section.Summary,
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryBrush,
                Margin = new Thickness(0, 0, 0, 6),
            });
        }
    }

    /// <summary>1 mục: tên đậm + các "phím" nhỏ + mô tả bên dưới.</summary>
    private static UIElement BuildItem(HelpItem item)
    {
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        title.Children.Add(new TextBlock { Text = item.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        foreach (var key in item.Keys)
        {
            title.Children.Add(new Border
            {
                Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
                BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1, 1, 1, 2),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = key, FontSize = 12 },
            });
        }

        return new Border
        {
            Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    title,
                    new TextBlock { Text = item.Description, TextWrapping = TextWrapping.Wrap, Foreground = SecondaryBrush, IsTextSelectionEnabled = true },
                },
            },
        };
    }

    private static Brush SecondaryBrush => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
    private static Brush AccentBrush { get; } = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x2B, 0x7B, 0xD6));
}
