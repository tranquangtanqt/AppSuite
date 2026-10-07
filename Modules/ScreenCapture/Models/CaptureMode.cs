namespace ScreenCapture.Models;

/// <summary>Kiểu chụp - ghi vào <see cref="CaptureInfo"/> (thẻ {mode} của mẫu tên file).</summary>
public enum CaptureMode
{
    FullScreen,
    /// <summary>Màn hình đang có con trỏ chuột (dùng nhiều màn hình; Toàn màn hình gộp mọi màn hình thành 1 ảnh).</summary>
    Monitor,
    Window,
    Region,
    FixedRegion,
    Scroll,
}
