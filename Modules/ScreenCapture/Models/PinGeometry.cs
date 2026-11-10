namespace ScreenCapture.Models;

/// <summary>Hình chữ nhật px thật trên màn hình ảo (X, Y = góc trên-trái).</summary>
public readonly record struct PinRect(int X, int Y, int Width, int Height);

/// <summary>
/// Tính toán cho ảnh ghim (Views/PinnedImageWindow.cs, 2026-11-10): phóng to / thu nhỏ bằng lăn chuột, độ mờ bằng Ctrl + lăn
/// chuột, chỗ đặt lúc mới ghim. Tách khỏi cửa sổ để unit test được.
/// </summary>
public static class PinGeometry
{
    public const double MinZoom = 0.1, MaxZoom = 4.0, ZoomStep = 1.1;
    public const int MinOpacity = 20, MaxOpacity = 100, OpacityStep = 10;

    /// <summary>Viền màu chủ đạo quanh ảnh (px) - phân biệt ảnh ghim với màn hình thật.</summary>
    public const int Border = 1;

    /// <summary>Ảnh chụp lớn hơn tỉ lệ này của vùng làm việc (vd Toàn màn hình) thì ghim thu nhỏ ở giữa màn hình, không đè kín.</summary>
    public const double LargeRatio = 0.8;

    /// <summary>Lăn 1 nấc (120) lên = phóng to 10%, xuống = thu nhỏ; đi ngang qua 100% thì dừng ở đúng 100%.</summary>
    public static double NextZoom(double zoom, int wheelDelta)
    {
        if (wheelDelta == 0)
        {
            return zoom;
        }
        double next = wheelDelta > 0 ? zoom * ZoomStep : zoom / ZoomStep;
        if ((zoom < 1 && next > 1) || (zoom > 1 && next < 1))
        {
            next = 1;
        }
        else if (Math.Abs(next - 1) < 0.01)
        {
            next = 1;
        }
        return Math.Clamp(next, MinZoom, MaxZoom);
    }

    /// <summary>Ctrl + lăn lên = rõ hơn 10%, xuống = mờ hơn; 20–100%.</summary>
    public static int NextOpacity(int percent, int wheelDelta) =>
        wheelDelta == 0 ? percent : Math.Clamp(percent + (wheelDelta > 0 ? OpacityStep : -OpacityStep), MinOpacity, MaxOpacity);

    /// <summary>Cỡ cửa sổ (cả viền) của ảnh <paramref name="imageWidth"/>×<paramref name="imageHeight"/> ở mức zoom.</summary>
    public static (int Width, int Height) WindowSize(int imageWidth, int imageHeight, double zoom) =>
        (Math.Max(1, (int)Math.Round(imageWidth * zoom)) + 2 * Border, Math.Max(1, (int)Math.Round(imageHeight * zoom)) + 2 * Border);

    /// <summary>Đổi cỡ cửa sổ mà điểm ảnh dưới con trỏ đứng yên (zoom quanh con trỏ).</summary>
    public static PinRect ResizeAround(PinRect window, int cursorX, int cursorY, int newWidth, int newHeight)
    {
        double fx = window.Width > 0 ? (cursorX - window.X) / (double)window.Width : 0.5;
        double fy = window.Height > 0 ? (cursorY - window.Y) / (double)window.Height : 0.5;
        return new PinRect((int)Math.Round(cursorX - fx * newWidth), (int)Math.Round(cursorY - fy * newHeight), newWidth, newHeight);
    }

    /// <summary>
    /// Chỗ đặt + zoom lúc mới ghim. Biết vùng vừa chụp (<paramref name="captured"/>) và ảnh không quá lớn → đè đúng chỗ đó ở 100%
    /// (viền nằm ngoài ảnh 1 px). Không biết chỗ (chụp cuộn, ghim từ Editor) hoặc ảnh lớn hơn <see cref="LargeRatio"/> vùng làm
    /// việc → giữa <paramref name="workArea"/>, thu nhỏ cho vừa nửa vùng làm việc nếu cần.
    /// </summary>
    public static (PinRect Window, double Zoom) Initial(int imageWidth, int imageHeight, PinRect? captured, PinRect workArea)
    {
        bool large = imageWidth > workArea.Width * LargeRatio || imageHeight > workArea.Height * LargeRatio;
        if (captured is { } at && !large)
        {
            var (w, h) = WindowSize(imageWidth, imageHeight, 1);
            return (new PinRect(at.X - Border, at.Y - Border, w, h), 1);
        }
        double zoom = large ? Math.Min(workArea.Width * 0.5 / imageWidth, workArea.Height * 0.5 / imageHeight) : 1;
        zoom = Math.Clamp(zoom, MinZoom, 1);
        var (width, height) = WindowSize(imageWidth, imageHeight, zoom);
        return (new PinRect(workArea.X + (workArea.Width - width) / 2, workArea.Y + (workArea.Height - height) / 2, width, height), zoom);
    }
}
