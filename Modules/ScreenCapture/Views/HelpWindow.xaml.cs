using System.Globalization;
using System.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScreenCapture.Models;
using ScreenCapture.Services.Interop;
using Windows.Graphics;
using WinRT.Interop;

namespace ScreenCapture.Views;

/// <summary>Cửa sổ Hướng dẫn (nút "Hướng dẫn" / F1): liệt kê mọi tính năng theo danh mục, có ô tìm kiếm
/// (không phân biệt hoa thường / dấu tiếng Việt: "cat" khớp "Cắt"). Nội dung ở <see cref="HelpContent"/>.</summary>
public sealed partial class HelpWindow : Window
{
    private IReadOnlyList<HelpSection> _sections;

    public HelpWindow(AppSettings settings)
    {
        InitializeComponent();
        SharedUI.Helpers.WindowIcon.Apply(this);
        _sections = HelpContent.Build(settings);

        // 1000×700 logic nhưng không vượt 90% vùng làm việc (màn 1920 ở 150% chỉ cao ~690 logic), căn giữa.
        var scale = NativeMethods.GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int width = (int)Math.Min(1000 * scale, work.Width * 0.9);
        int height = (int)Math.Min(700 * scale, work.Height * 0.9);
        AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));

        foreach (var section in _sections)
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
        SectionList.SelectedIndex = 0;
    }

    /// <summary>Phím tắt đổi trong Cài đặt → dựng lại nội dung (mục "Phím tắt chụp" lấy theo cài đặt).</summary>
    public void UpdateSettings(AppSettings settings)
    {
        _sections = HelpContent.Build(settings);
        for (int i = 0; i < _sections.Count && i < SectionList.Items.Count; i++)
        {
            ((ListViewItem)SectionList.Items[i]).Tag = _sections[i];
        }
        Render();
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

        string query = Normalize(SearchBox.Text.Trim());
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

        // Mọi từ trong ô tìm kiếm đều phải xuất hiện (ở tên, mô tả, phím hoặc tên danh mục).
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int count = 0;
        foreach (var section in _sections)
        {
            var matches = section.Items
                .Where(item => words.All(Normalize($"{section.Title} {item.Name} {item.Description} {string.Join(' ', item.Keys)}").Contains))
                .ToList();
            if (matches.Count == 0)
            {
                continue;
            }
            AddSectionHeader(section, large: false);
            foreach (var item in matches)
            {
                ContentPanel.Children.Add(BuildItem(item));
            }
            count += matches.Count;
        }
        ContentPanel.Children.Insert(0, new TextBlock
        {
            Text = count == 0 ? $"Không tìm thấy tính năng nào khớp \"{SearchBox.Text.Trim()}\"." : $"{count} kết quả cho \"{SearchBox.Text.Trim()}\"",
            Foreground = SecondaryBrush,
            Margin = new Thickness(0, 0, 0, 4),
        });
    }

    private void AddSectionHeader(HelpSection section, bool large)
    {
        ContentPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, large || ContentPanel.Children.Count == 0 ? 0 : 14, 0, 0),
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

    /// <summary>1 tính năng: tên đậm + các phím tắt dạng "phím bàn phím" + mô tả bên dưới.</summary>
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
    private static Brush AccentBrush { get; } = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xD8, 0x64, 0x45));

    /// <summary>Chữ thường, bỏ dấu tiếng Việt (đ → d) để tìm "cat" ra "Cắt", "mui ten" ra "Mũi tên".</summary>
    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
