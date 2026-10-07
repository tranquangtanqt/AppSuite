namespace ScreenCapture.Models;

/// <summary>Thông tin 1 lần chụp, dùng để đặt tên file khi tự lưu (<see cref="FileNameTemplate"/>): thời điểm, kiểu chụp,
/// chương trình + tiêu đề của cửa sổ bị chụp (rỗng nếu không xác định được - vd vùng chọn rơi vào desktop).</summary>
public sealed record CaptureInfo(DateTime Time, CaptureMode Mode, string App, string Window)
{
    /// <summary>Ảnh không phải chụp (mở file, ảnh mới) hoặc chưa biết cửa sổ.</summary>
    public static CaptureInfo Now(CaptureMode mode) => new(DateTime.Now, mode, string.Empty, string.Empty);
}
