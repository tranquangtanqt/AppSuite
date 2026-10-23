using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace MainLauncher.Services;

/// <summary>Đóng MainLauncher mà còn module đang chạy thì làm gì.</summary>
public enum CloseModulesOnExit
{
    /// <summary>Hỏi "Đóng hết / Để chạy tiếp / Huỷ" (mặc định).</summary>
    Ask,
    CloseAll,
    KeepRunning,
}

/// <summary>Thiết lập riêng của launcher do người dùng chọn (tách khỏi modules.json để cập nhật / publish lại modules.json
/// không làm mất lựa chọn).</summary>
public sealed class LauncherSettings
{
    /// <summary>Tên module (ModuleConfig.Name) bị ẩn khỏi Dashboard và Module List (Settings vẫn liệt kê đủ để bật lại).</summary>
    public List<string> HiddenModules { get; set; } = [];

    public CloseModulesOnExit CloseModulesOnExit { get; set; } = CloseModulesOnExit.Ask;

    /// <summary>Thứ tự module do người dùng kéo thả (tên module). Trống = theo modules.json; module không có ở đây đứng sau.</summary>
    public List<string> ModuleOrder { get; set; } = [];

    /// <summary>Khi Windows mở MainLauncher lúc đăng nhập (--startup): thu nhỏ xuống thanh tác vụ thay vì mở to. Bật / tắt
    /// "Khởi động cùng Windows" nằm ở registry (StartupRegistration), không ở đây.</summary>
    public bool StartMinimized { get; set; } = true;

    /// <summary>Bấm X: ẩn cửa sổ xuống khay hệ thống (module chạy tiếp; thoát hẳn bằng menu chuột phải ở icon khay) thay vì thoát.
    /// Bật thì icon ở khay luôn hiện khi MainLauncher đang chạy.</summary>
    public bool CloseToTray { get; set; } = true;
}

/// <summary>Đọc 1 lần Config\launcher-settings.json cạnh exe (<see cref="Current"/>, dùng chung cho cả app) và ghi lại khi
/// đổi. File thiếu / hỏng → thiết lập mặc định.</summary>
public sealed class LauncherSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ILogger<LauncherSettingsStore> _logger;

    public LauncherSettingsStore(string path, ILogger<LauncherSettingsStore> logger)
    {
        Path = path;
        _logger = logger;
        Current = Load();
    }

    public string Path { get; }

    public LauncherSettings Current { get; }

    /// <summary>Sau mỗi lần <see cref="Save"/> (vd cửa sổ chính bật / tắt icon khay theo CloseToTray).</summary>
    public event Action? Saved;

    private LauncherSettings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(Path), JsonOptions) ?? new LauncherSettings();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không đọc được {Path} - dùng thiết lập mặc định", Path);
        }
        return new LauncherSettings();
    }

    /// <summary>Ghi <see cref="Current"/> ra file tạm rồi thay, để lỗi giữa chừng không làm hỏng file cũ.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(temp, Path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không lưu được {Path}", Path);
        }
        Saved?.Invoke();
    }
}
