using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace SharedUI.Help;

/// <summary>1 tính năng trong cửa sổ Hướng dẫn. <paramref name="Keys"/>: phím tắt / thao tác chuột hiện thành các "phím"
/// nhỏ (vd "Ctrl+Z").</summary>
public sealed record HelpItem(string Name, string Description, params string[] Keys);

/// <summary>1 danh mục bên trái cửa sổ Hướng dẫn. <paramref name="Glyph"/>: ký tự Segoe Fluent Icons.</summary>
public sealed record HelpSection(string Title, string Glyph, string Summary, IReadOnlyList<HelpItem> Items);

/// <summary>
/// Cửa sổ Hướng dẫn (F1) dùng chung: danh mục bên trái, nội dung bên phải, ô tìm kiếm không phân biệt hoa thường / dấu
/// tiếng Việt ("cat" khớp "Cắt"). Mỗi module chỉ cung cấp nội dung (<see cref="HelpSection"/>).
/// Dựng hoàn toàn bằng code (không .xaml) để thư viện không cần XBF / .pri riêng cho cửa sổ này.
/// </summary>
public sealed class HelpWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private IReadOnlyList<HelpSection> _sections;
    private readonly bool _searchSummary;
    private readonly Brush _accent;
    private readonly ListView _sectionList;
    private readonly TextBox _searchBox;
    private readonly ScrollViewer _contentScroller;
    private readonly StackPanel _contentPanel = new() { Spacing = 10, Padding = new Thickness(0, 0, 0, 16) };

    /// <param name="appName">Tên module, hiện ở tiêu đề cửa sổ và "Hướng dẫn sử dụng …".</param>
    /// <param name="subtitle">1 dòng mô tả dưới tiêu đề.</param>
    /// <param name="accent">Màu nhấn của module (ô icon trên cùng, icon danh mục); null = màu nhấn của Windows.</param>
    /// <param name="searchPlaceholder">Gợi ý trong ô tìm, vd "Tìm tính năng, vd: lọc, Ctrl+G...".</param>
    /// <param name="iconPath">File .ico cho thanh tiêu đề / taskbar (null = icon của app, xem <see cref="Helpers.WindowIcon"/>).</param>
    /// <param name="searchSummary">Tìm cả trong dòng tóm tắt của danh mục (từ khớp tóm tắt → mọi mục của danh mục đó khớp
    /// từ đó) - cho danh mục mỗi cái là 1 trang / 1 công cụ (FileTools: "healthcheck" chỉ có trong tóm tắt trang Tìm / Lọc dòng).</param>
    public HelpWindow(string appName, string subtitle, IReadOnlyList<HelpSection> sections, Color? accent = null,
        string searchPlaceholder = "Tìm tính năng...", string? iconPath = null, bool searchSummary = false)
    {
        _sections = sections;
        _searchSummary = searchSummary;
        _accent = accent is { } color
            ? new SolidColorBrush(color)
            : (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        Title = $"Hướng dẫn - {appName}";
        SystemBackdrop = new MicaBackdrop();
        Helpers.WindowIcon.Apply(this, iconPath); // null = icon của app (Assets\<tên exe>.ico)

        _searchBox = new TextBox
        {
            Width = 300,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            PlaceholderText = searchPlaceholder,
        };
        _searchBox.TextChanged += (_, _) => SearchChanged();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_searchBox, "HelpSearchBox");

        _sectionList = new ListView
        {
            Background = Resource("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = Resource("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(0, 6, 0, 6),
        };
        _sectionList.SelectionChanged += (_, _) => SectionChanged();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_sectionList, "HelpSectionList");

        _contentScroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(24, 16, 24, 16), Content = _contentPanel };

        Content = BuildLayout(appName, subtitle);

        // 1000×700 logic nhưng không vượt 90% vùng làm việc (màn nhỏ ở 150% chỉ cao ~600 logic), căn giữa.
        var scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int width = (int)Math.Min(1000 * scale, work.Width * 0.9);
        int height = (int)Math.Min(700 * scale, work.Height * 0.9);
        AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));

        foreach (var section in _sections)
        {
            _sectionList.Items.Add(new ListViewItem
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
        _sectionList.SelectedIndex = 0;
    }

    /// <summary>Mở danh mục có tiêu đề <paramref name="title"/> (vd mở Hướng dẫn đúng phần đang dùng) và cuộn danh sách
    /// danh mục tới đó. Không có thì bỏ qua.</summary>
    public void ShowSection(string title)
    {
        _searchBox.Text = string.Empty;
        var item = _sectionList.Items.OfType<ListViewItem>().FirstOrDefault(i => i.Tag is HelpSection s && s.Title == title);
        if (item is not null)
        {
            _sectionList.SelectedItem = item;
            // Sau khi danh sách đã bố cục - gọi ngay lúc vừa tạo cửa sổ thì không có tác dụng.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _sectionList.ScrollIntoView(item));
        }
    }

    /// <summary>Thay nội dung khi cửa sổ đang mở (vd ScreenCapture: đổi phím tắt trong Cài đặt → mục "Phím tắt chụp" đổi
    /// theo). Cùng danh sách danh mục, chỉ khác nội dung: giữ danh mục / ô tìm đang xem.</summary>
    public void UpdateSections(IReadOnlyList<HelpSection> sections)
    {
        _sections = sections;
        for (int i = 0; i < _sections.Count && i < _sectionList.Items.Count; i++)
        {
            ((ListViewItem)_sectionList.Items[i]).Tag = _sections[i];
        }
        Render();
    }

    private Grid BuildLayout(string appName, string subtitle)
    {
        var root = new Grid { Padding = new Thickness(16), RowSpacing = 12, ColumnSpacing = 16 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new Border
                {
                    Width = 40,
                    Height = 40,
                    CornerRadius = new CornerRadius(10),
                    Background = _accent,
                    Child = new FontIcon { Glyph = "", FontSize = 20, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) },
                },
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = $"Hướng dẫn sử dụng {appName}", FontSize = 20, FontWeight = FontWeights.SemiBold },
                        new TextBlock { Text = subtitle, FontSize = 12, Foreground = SecondaryBrush, TextWrapping = TextWrapping.Wrap },
                    },
                },
            },
        };
        Grid.SetColumnSpan(header, 2);
        root.Children.Add(header);

        Grid.SetColumn(_searchBox, 1);
        root.Children.Add(_searchBox);

        Grid.SetRow(_sectionList, 1);
        root.Children.Add(_sectionList);

        var contentCard = new Border
        {
            Background = Resource("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = Resource("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = _contentScroller,
        };
        Grid.SetRow(contentCard, 1);
        Grid.SetColumn(contentCard, 1);
        root.Children.Add(contentCard);
        return root;
    }

    private void SectionChanged()
    {
        if (_sectionList.SelectedItem is not null && _searchBox.Text.Length > 0)
        {
            _searchBox.Text = string.Empty; // chọn danh mục = thoát tìm kiếm (TextChanged sẽ Render)
            return;
        }
        Render();
    }

    private void SearchChanged()
    {
        if (_searchBox.Text.Trim().Length > 0)
        {
            _sectionList.SelectedItem = null;
        }
        else if (_sectionList.SelectedItem is null)
        {
            _sectionList.SelectedIndex = 0;
        }
        Render();
    }

    private void Render()
    {
        _contentPanel.Children.Clear();
        _contentScroller.ChangeView(null, 0, null, true);

        string query = Normalize(_searchBox.Text.Trim());
        if (query.Length == 0)
        {
            if (_sectionList.SelectedItem is ListViewItem { Tag: HelpSection section })
            {
                AddSectionHeader(section, large: true);
                foreach (var item in section.Items)
                {
                    _contentPanel.Children.Add(BuildItem(item));
                }
            }
            return;
        }

        // Mọi từ trong ô tìm kiếm đều phải xuất hiện (ở tên, mô tả, phím hoặc tên danh mục - và tóm tắt nếu bật).
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int count = 0;
        foreach (var section in _sections)
        {
            string sectionText = _searchSummary ? $"{section.Title} {section.Summary}" : section.Title;
            var matches = section.Items
                .Where(item => words.All(Normalize($"{sectionText} {item.Name} {item.Description} {string.Join(' ', item.Keys)}").Contains))
                .ToList();
            if (matches.Count == 0)
            {
                continue;
            }
            AddSectionHeader(section, large: false);
            foreach (var item in matches)
            {
                _contentPanel.Children.Add(BuildItem(item));
            }
            count += matches.Count;
        }
        _contentPanel.Children.Insert(0, new TextBlock
        {
            Text = count == 0 ? $"Không tìm thấy tính năng nào khớp \"{_searchBox.Text.Trim()}\"." : $"{count} kết quả cho \"{_searchBox.Text.Trim()}\"",
            Foreground = SecondaryBrush,
            Margin = new Thickness(0, 0, 0, 4),
        });
    }

    private void AddSectionHeader(HelpSection section, bool large)
    {
        _contentPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, large || _contentPanel.Children.Count == 0 ? 0 : 14, 0, 0),
            Children =
            {
                new FontIcon { Glyph = section.Glyph, FontSize = large ? 20 : 16, Foreground = _accent },
                new TextBlock { Text = section.Title, FontSize = large ? 20 : 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center },
            },
        });
        if (large)
        {
            _contentPanel.Children.Add(new TextBlock
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
                Background = Resource("ControlFillColorSecondaryBrush"),
                BorderBrush = Resource("ControlStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1, 1, 1, 2),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = key, FontSize = 12 },
            });
        }

        var body = new StackPanel { Spacing = 4, Children = { title } };
        if (item.Description.Length > 0) // mục chỉ có phím (vd danh mục "Phím tắt") thì không có dòng mô tả trống
        {
            body.Children.Add(new TextBlock { Text = item.Description, TextWrapping = TextWrapping.Wrap, Foreground = SecondaryBrush, IsTextSelectionEnabled = true });
        }

        return new Border
        {
            Background = Resource("LayerFillColorDefaultBrush"),
            BorderBrush = Resource("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10),
            Child = body,
        };
    }

    private static Brush Resource(string key) => (Brush)Application.Current.Resources[key];

    private static Brush SecondaryBrush => Resource("TextFillColorSecondaryBrush");

    /// <summary>Chữ thường, bỏ dấu tiếng Việt (đ → d) để tìm "cat" ra "Cắt", "mui ten" ra "Mũi tên".</summary>
    internal static string Normalize(string text)
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
