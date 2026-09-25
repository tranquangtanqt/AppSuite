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

    // ---- Vùng cố định (không có trên cửa sổ Cài đặt - app tự ghi mỗi lần chụp Vùng cố định) ----
    /// <summary>Vùng cố định lần gần nhất (toạ độ màn hình ảo), để lần chụp sau - kể cả sau khi tắt mở
    /// lại app - hiện sẵn đúng vùng đó. null = chưa chụp lần nào.</summary>
    public ScreenRegion? LastFixedRegion { get; set; }

    // ---- Chụp cuộn (xem Services/ScrollCaptureService.cs) ----
    /// <summary>Số lần cuộn tối đa. 150: vùng chọn thấp/hẹp thì mỗi bước cuộn ít (đã gặp khi test cuộn
    /// ngang: 80 bước chưa hết nội dung).</summary>
    public int ScrollMaxSteps { get; set; } = 150;
    /// <summary>Độ dài ảnh ghép tối đa theo chiều cuộn (px) - cao khi cuộn dọc, rộng khi cuộn ngang.</summary>
    public int ScrollMaxLength { get; set; } = 30000;
    /// <summary>Chờ sau mỗi lần lăn chuột trước khi chụp (ms) - tăng lên cho trang tải/vẽ chậm.</summary>
    public int ScrollSettleMs { get; set; } = 450;

    public static readonly (int Min, int Max) ScrollMaxStepsRange = (10, 1000);
    // Trên 60.000px ảnh ghép (vd 2000px × 60.000px ≈ 480 MB RAM) dễ làm Editor chậm / hết bộ nhớ.
    public static readonly (int Min, int Max) ScrollMaxLengthRange = (2000, 60000);
    // Tối thiểu 200ms: chờ quá ngắn thì trang cuộn mượt / vẽ chậm chưa kịp đổi → tưởng đã tới cuối và dừng sớm.
    public static readonly (int Min, int Max) ScrollSettleMsRange = (200, 3000);

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
        new() { Action = HotkeyAction.ScrollCaptureHorizontal, Key = HotkeyBinding.NoKey },
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
    ScrollCaptureHorizontal,
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

/// <summary>Hình chữ nhật trên màn hình ảo (px thật) - dạng lưu được vào settings.json.</summary>
public sealed class ScreenRegion
{
    public int Left { get; set; }
    public int Top { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }
}
