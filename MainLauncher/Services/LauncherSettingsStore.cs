using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MainLauncher.Services;

/// <summary>Thiết lập riêng của launcher do người dùng chọn (tách khỏi modules.json để cập nhật / publish lại modules.json
/// không làm mất lựa chọn).</summary>
public sealed class LauncherSettings
{
    /// <summary>Tên module (ModuleConfig.Name) bị ẩn khỏi Dashboard và Module List (Settings vẫn liệt kê đủ để bật lại).</summary>
    public List<string> HiddenModules { get; set; } = [];
}

/// <summary>Đọc / ghi Config\launcher-settings.json cạnh exe. File thiếu / hỏng → thiết lập mặc định (hiện hết).</summary>
public sealed class LauncherSettingsStore(string path, ILogger<LauncherSettingsStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string Path { get; } = path;

    public LauncherSettings Load()
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
            logger.LogWarning(ex, "Không đọc được {Path} - dùng thiết lập mặc định", Path);
        }
        return new LauncherSettings();
    }

    /// <summary>Ghi file tạm rồi thay, để lỗi giữa chừng không làm hỏng file cũ.</summary>
    public void Save(LauncherSettings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, Path, overwrite: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Không lưu được {Path}", Path);
        }
    }
}
