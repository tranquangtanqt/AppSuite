namespace ScreenCapture.Models;

/// <summary>Tuỳ chọn của app (cửa sổ Cài đặt), lưu ở Data\Config\settings.json cạnh exe
/// (xem Services/SettingsStore.cs).</summary>
public sealed class AppSettings
{
    // ---- Chung ----
    /// <summary>Chờ thêm N giây trước khi chụp (để kịp mở menu, hover...). 0 = chụp ngay.</summary>
    public int CaptureDelaySeconds { get; set; }

    /// <summary>Chụp xong tự copy ảnh vào clipboard (ngoài việc mở trong Editor).</summary>
    public bool CopyToClipboardAfterCapture { get; set; }

    /// <summary>Hiện icon ở khay hệ thống; bấm X ở cửa sổ chính thì ẩn xuống khay (phím tắt vẫn chạy)
    /// thay vì thoát. Thoát hẳn qua menu chuột phải của icon.</summary>
    public bool RunInTray { get; set; } = true;

    /// <summary>Tự chạy khi đăng nhập Windows (HKCU\...\Run, tham số --tray: chạy ngầm ở khay luôn).</summary>
    public bool StartWithWindows { get; set; }

    // ---- Tự động lưu ----
    public bool AutoSave { get; set; }
    public string AutoSaveFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ScreenCapture");

    // ---- Phiên làm việc (nhớ tab khi tắt app, xem SessionService) ----
    public bool RememberTabs { get; set; } = true;
    public int SessionMaxTabs { get; set; } = 30;
    public int SessionMaxMegabytes { get; set; } = 300;

    // ---- Phím tắt toàn cục ----
    public List<HotkeyBinding> Hotkeys { get; set; } = DefaultHotkeys();

    /// <summary>Mặc định giống PicPick.</summary>
    public static List<HotkeyBinding> DefaultHotkeys() =>
    [
        new() { Action = HotkeyAction.FullScreen, Key = "PrintScreen" },
        new() { Action = HotkeyAction.ActiveWindow, Alt = true, Key = "PrintScreen" },
        new() { Action = HotkeyAction.Region, Shift = true, Key = "PrintScreen" },
        new() { Action = HotkeyAction.FixedRegion, Shift = true, Ctrl = true, Key = "PrintScreen" },
        new() { Action = HotkeyAction.ScrollCapture, Ctrl = true, Alt = true, Key = "PrintScreen" },
        new() { Action = HotkeyAction.RepeatLast, Key = HotkeyBinding.NoKey },
    ];

    /// <summary>Luôn có đủ 1 binding cho mỗi HotkeyAction (file cũ thiếu action mới thì bổ sung mặc định).</summary>
    public HotkeyBinding GetHotkey(HotkeyAction action)
    {
        var binding = Hotkeys.FirstOrDefault(h => h.Action == action);
        if (binding is null)
        {
            binding = DefaultHotkeys().First(h => h.Action == action);
            Hotkeys.Add(binding);
        }
        return binding;
    }

    public AppSettings Clone()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.Hotkeys = Hotkeys.Select(h => h.Clone()).ToList();
        return copy;
    }
}

public enum HotkeyAction
{
    FullScreen,
    ActiveWindow,
    Region,
    FixedRegion,
    RepeatLast,
    ScrollCapture,
}

public sealed class HotkeyBinding
{
    public const string NoKey = "None";

    public HotkeyAction Action { get; set; }
    public bool Shift { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public string Key { get; set; } = NoKey;

    public bool IsEnabled => Key != NoKey && VirtualKeyOf(Key) != 0;

    public HotkeyBinding Clone() => (HotkeyBinding)MemberwiseClone();

    public string Describe() => IsEnabled
        ? string.Join("+", new[] { Ctrl ? "Ctrl" : null, Shift ? "Shift" : null, Alt ? "Alt" : null, Key }.OfType<string>())
        : "(không dùng)";

    /// <summary>Phím chọn được trong ComboBox của trang Phím tắt.</summary>
    public static IReadOnlyList<string> AvailableKeys { get; } =
    [
        NoKey, "PrintScreen",
        .. Enumerable.Range('A', 26).Select(c => ((char)c).ToString()),
        .. Enumerable.Range('0', 10).Select(c => ((char)c).ToString()),
        .. Enumerable.Range(1, 12).Select(n => $"F{n}"),
    ];

    /// <summary>Tên phím → mã Virtual-Key của Win32 (0 = không hợp lệ).</summary>
    public static uint VirtualKeyOf(string key) => key switch
    {
        "PrintScreen" => 0x2C,
        { Length: 1 } when char.IsAsciiLetterUpper(key[0]) || char.IsAsciiDigit(key[0]) => key[0],
        ['F', .. var n] when int.TryParse(n, out int f) && f is >= 1 and <= 12 => (uint)(0x70 + f - 1),
        _ => 0,
    };
}
