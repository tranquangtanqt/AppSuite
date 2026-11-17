namespace ScreenCapture.Models;

/// <summary>
/// Kích thước (DIP) cho SKXamlCanvas để ảnh không bị nhoè ở màn hình phóng to (2026-11-17).
/// SKXamlCanvas tạo bitmap <c>(int)(ActualWidth × scale)</c> pixel rồi kéo giãn cho đầy control: vd khung cần 332 px ở 150%
/// → 221,333 DIP × 1,5 = 331,99… → bitmap 331 px kéo ra 332 px → mép ảnh có 1 hàng pixel màu pha.
/// Chọn số DIP nguyên (số pixel = bội của 3 ở 150%, của 5 ở 125%...) ≥ số pixel cần → nhân lại luôn tròn, vẽ đúng 1:1.
/// Khung lớn hơn nội dung vài pixel (phần trong suốt ở mép phải / dưới).
/// </summary>
public static class CanvasSize
{
    public static double ExactDip(double physicalPixels, double scale)
    {
        if (scale <= 0)
        {
            return physicalPixels;
        }
        int needed = (int)Math.Ceiling(physicalPixels - 0.001);
        for (int pixels = needed; pixels < needed + 16; pixels++)
        {
            double dip = pixels / scale;
            if (Math.Abs(dip - Math.Round(dip)) < 1e-6)
            {
                return Math.Round(dip);
            }
        }
        return needed / scale; // tỉ lệ lạ (không có số DIP nguyên gần): giữ như cũ
    }
}
