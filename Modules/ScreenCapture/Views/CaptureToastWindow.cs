using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScreenCapture.Services.Interop;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using Windows.Graphics;
using WinRT.Interop;

namespace ScreenCapture.Views;

/// <summary>Thông báo nhỏ sau khi chụp (Cài đặt > Chung > Sau khi chụp > Hiện thông báo): góc dưới-phải màn hình đang có
/// con trỏ, nằm trên cùng nhưng KHÔNG giành focus (đang gõ dở ở app khác vẫn gõ tiếp). Ảnh thu nhỏ + việc đã làm (đã copy /
/// đã lưu vào đâu / lỗi) + nút Mở trong Editor (khi Editor không tự mở) / Mở thư mục (khi đã lưu). Tự đóng sau 6 giây,
/// rê chuột vào thì giữ. Launcher đóng nó trước mỗi lần chụp để không lọt vào ảnh.</summary>
public sealed class CaptureToastWindow : Window
{
    private const int WidthDip = 440, HeightDip = 128, MarginDip = 16; // đủ 2 nút ở màn 150% (380 thì cắt "Mở thư mục")
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(6);

    private readonly DispatcherQueueTimer _timer;

    /// <param name="lines">Các dòng trạng thái (đã copy, đã lưu..., lỗi).</param>
    /// <param name="openEditor">Null = không hiện nút Mở trong Editor (Editor đã tự mở).</param>
    /// <param name="savedPath">File đã tự lưu - hiện nút Mở thư mục (chọn sẵn file).</param>
    public CaptureToastWindow(SKBitmap bitmap, string title, IReadOnlyList<string> lines, Action? openEditor, string? savedPath)
    {
        Title = "ScreenCapture - đã chụp";
        SharedUI.Helpers.WindowIcon.Apply(this);

        var thumbnail = new Image
        {
            Source = Thumbnail(bitmap).ToWriteableBitmap(),
            Stretch = Stretch.Uniform,
            Width = 120,
            Height = 96,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        foreach (var line in lines)
        {
            text.Children.Add(new TextBlock
            {
                Text = line,
                FontSize = 12,
                Opacity = 0.8,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            });
            ToolTipService.SetToolTip(text.Children[^1], line);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
        if (openEditor is not null)
        {
            buttons.Children.Add(MakeButton("Mở trong Editor", "OpenEditorButton", () => openEditor()));
        }
        if (savedPath is not null)
        {
            buttons.Children.Add(MakeButton("Mở thư mục", "OpenFolderButton",
                () => System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{savedPath}\"")));
        }
        text.Children.Add(buttons);

        var close = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 10 },
            Padding = new Thickness(6),
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(close, "CloseToastButton");
        ToolTipService.SetToolTip(close, "Đóng");
        close.Click += (_, _) => Close();

        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(12, 10, 6, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(thumbnail);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);

        var root = new Border
        {
            Child = grid,
            Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
        };
        // Rê chuột vào thì giữ thông báo, rời ra thì đếm lại.
        root.PointerEntered += (_, _) => _timer?.Stop();
        root.PointerExited += (_, _) => _timer?.Start();
        Content = root;

        // Không có hiệu ứng mờ dần khi đóng: launcher đóng thông báo rồi chụp ngay (~200 ms) - đang mờ dần sẽ lọt vào ảnh
        // (đã gặp: góc ảnh sẫm 5%).
        int disable = 1;
        NativeMethods.DwmSetWindowAttributeInt(WindowNative.GetWindowHandle(this), NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLED, ref disable, sizeof(int));
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(true, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }
        PlaceAtCorner();

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = Lifetime;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Close();
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>Hiện mà không lấy focus.</summary>
    public void ShowWithoutFocus()
    {
        AppWindow.Show(activateWindow: false);
        _timer.Start();
    }

    private Button MakeButton(string text, string automationId, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 3, 10, 4), FontSize = 12 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, automationId);
        button.Click += (_, _) =>
        {
            action();
            Close();
        };
        return button;
    }

    /// <summary>Góc dưới-phải vùng làm việc (trừ taskbar) của màn hình đang có con trỏ.</summary>
    private void PlaceAtCorner()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var area = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest).WorkArea;
        double scale = NativeMethods.GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        // Cửa sổ vừa tạo nằm ở màn hình chính - màn đích khác DPI thì lấy tỉ lệ theo màn đích.
        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (NativeMethods.GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0)
        {
            scale = dpiX / 96.0;
        }
        int width = (int)(WidthDip * scale), height = (int)(HeightDip * scale), margin = (int)(MarginDip * scale);
        AppWindow.MoveAndResize(new RectInt32(area.X + area.Width - width - margin, area.Y + area.Height - height - margin, width, height));
    }

    /// <summary>Ảnh thu nhỏ (cạnh dài ≤ 240 px) - không đưa nguyên ảnh 4K vào ô 120 px.</summary>
    private static SKBitmap Thumbnail(SKBitmap source)
    {
        double ratio = Math.Min(1.0, 240.0 / Math.Max(source.Width, source.Height));
        var info = new SKImageInfo(Math.Max(1, (int)(source.Width * ratio)), Math.Max(1, (int)(source.Height * ratio)));
        return source.Resize(info, SKFilterQuality.Medium) ?? source;
    }
}
