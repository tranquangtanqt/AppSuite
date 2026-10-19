using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenCapture.Models;

namespace ScreenCapture.Tests;

/// <summary>Đóng ảnh đã tự lưu (2026-10-19): file nào được đề nghị xoá, cờ tự lưu còn / mất sau khi lưu, cài đặt mặc định.</summary>
public class AutoSavedFileTests
{
    private const string Auto = @"C:\Pictures\ScreenCapture\2026-10-19_10-01-46.png";
    private static bool Exists(string _) => true;
    private static bool Missing(string _) => false;

    [Fact]
    public void Deletable_only_when_tab_still_points_at_the_auto_saved_file_and_it_exists()
    {
        Assert.Equal(Auto, AutoSavedFile.Deletable(Auto, Auto, Exists));
        Assert.Equal(Auto, AutoSavedFile.Deletable(Auto, Auto.ToUpperInvariant(), Exists)); // Windows: không phân biệt hoa thường
        Assert.Null(AutoSavedFile.Deletable(Auto, Auto, Missing));                        // đã bị xoá / đổi tên bên ngoài
        Assert.Null(AutoSavedFile.Deletable(null, Auto, Exists));                          // mở từ file có sẵn / tự Lưu thành
        Assert.Null(AutoSavedFile.Deletable(Auto, null, Exists));
        Assert.Null(AutoSavedFile.Deletable(Auto, @"D:\khac\anh.png", Exists));
    }

    [Fact]
    public void Saving_over_the_auto_saved_file_keeps_the_flag_save_as_elsewhere_drops_it()
    {
        Assert.Equal(Auto, AutoSavedFile.AfterSave(Auto, Auto));                 // Ctrl+S ghi đè đúng file tự lưu
        Assert.Null(AutoSavedFile.AfterSave(Auto, @"D:\bao cao\anh.png"));       // Lưu thành / Lưu tất cả sang file khác
        Assert.Null(AutoSavedFile.AfterSave(null, Auto));                        // chưa từng tự lưu
    }

    [Fact]
    public void Setting_defaults_to_keep_and_round_trips_as_text()
    {
        Assert.Equal(AutoSavedCloseAction.Keep, new AppSettings().AutoSavedCloseAction);
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        // settings.json cũ (chưa có mục này) → Giữ file như trước
        Assert.Equal(AutoSavedCloseAction.Keep, JsonSerializer.Deserialize<AppSettings>("{}", options)!.AutoSavedCloseAction);
        var json = JsonSerializer.Serialize(new AppSettings { AutoSavedCloseAction = AutoSavedCloseAction.Ask }, options);
        Assert.Contains("\"AutoSavedCloseAction\":\"Ask\"", json);
        Assert.Equal(AutoSavedCloseAction.Ask, JsonSerializer.Deserialize<AppSettings>(json, options)!.AutoSavedCloseAction);
    }
}
