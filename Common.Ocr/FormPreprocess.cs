using SkiaSharp;

namespace Common.Ocr;

/// <summary>Ảnh đen trắng sau xử lý: <see cref="Pixels"/> 0 = nét chữ, 255 = nền; đã phóng <see cref="Scale"/> lần so
/// với ảnh gốc.</summary>
public sealed record BinaryImage(byte[] Pixels, int Width, int Height, int Scale)
{
    public bool IsInk(int x, int y) => Pixels[y * Width + x] == 0;
}

/// <summary>Chuẩn bị ảnh chụp màn hình dạng form (nhiều ô nhập có viền) cho OCR - dùng ở chế độ So chữ.
///
/// Đo trên 2 ảnh màn hình nghiệp vụ tiếng Nhật (155 nhãn / giá trị, xem PLAN.md): đọc nguyên ảnh thì OCR chỉ đúng
/// 36–61% - viền ô nhập làm engine chia dòng sai và đọc số trong ô thành rác ("23ロ1ロ7"). Phóng ×3 → đen trắng
/// (ngưỡng chung) → xoá các đoạn kẻ dài (viền ô, đường phân cách) đưa Windows OCR lên 75–85%, Tesseract (tách cụm)
/// lên 62–77% (xem PLAN.md).</summary>
public static class FormPreprocess
{
    /// <summary>Phóng mặc định: chữ màn hình ~12 px → ~36 px, cỡ mà cả Windows OCR lẫn Tesseract đọc tốt.</summary>
    public const int DefaultScale = 3;

    /// <summary>Đoạn mực nằm ngang dài từ chừng này (px ảnh gốc) trở lên là viền / đường kẻ - chữ không có nét
    /// ngang liền dài như vậy.</summary>
    private const int MinHorizontalLine = 40;

    /// <summary>Đoạn mực dọc từ chừng này (px ảnh gốc) trở lên là viền ô - cao hơn 1 dòng chữ màn hình (~12–14 px).</summary>
    private const int MinVerticalLine = 16;

    /// <summary>Ngưỡng Otsu kẹp vào [185, 200]. Otsu thuần trên cả trang ra thấp (< 185) → chữ xám của ô bị khoá
    /// (#A0A0A0 = 160 sau khi phóng còn lẫn 160–255) bị xoá: kẹp sàn 185 đọc đúng thêm (116→123 / 131→133 nhãn) và bắt
    /// thêm chữ khác màu (6→9). Trần 200: vùng cắt nhỏ để đọc lại có thể chỉ gồm nền trắng + nền trang xanh nhạt
    /// (~246) - Otsu chia đôi 2 màu nền đó, nền thành mực.
    /// Đã thử và bỏ: ngưỡng cục bộ (pixel tối hơn trung bình quanh nó) - ghép được ít đoạn giống hơn (87–121 so với 130).</summary>
    private const int MinThreshold = 185, MaxThreshold = 200;

    /// <param name="threshold">Ngưỡng cố định thay cho Otsu - đọc lại vùng có chữ xám nét mảnh ở ngưỡng cao hơn để nét
    /// không bị đứt (xem TextDiffVerifier của ImageCompare).</param>
    public static BinaryImage Prepare(SKBitmap bitmap, int scale = DefaultScale, int? threshold = null)
    {
        var gray = ScaledGray(bitmap, scale, out int w, out int h);
        int cut = threshold ?? Math.Clamp(Otsu(gray), MinThreshold, MaxThreshold);
        var bin = new byte[gray.Length];
        for (int i = 0; i < bin.Length; i++)
        {
            bin[i] = gray[i] <= cut ? (byte)0 : (byte)255;
        }
        RemoveLines(bin, w, h, MinHorizontalLine * scale, MinVerticalLine * scale);
        return new BinaryImage(bin, w, h, scale);
    }

    /// <summary>Ngưỡng Otsu: chia histogram thành 2 nhóm (nét / nền) sao cho phương sai giữa 2 nhóm lớn nhất.</summary>
    private static int Otsu(byte[] gray)
    {
        var hist = new long[256];
        foreach (var v in gray)
        {
            hist[v]++;
        }
        long total = gray.Length, sumAll = 0;
        for (int i = 0; i < 256; i++)
        {
            sumAll += i * hist[i];
        }
        long weightBack = 0, sumBack = 0;
        double best = -1;
        int threshold = 128;
        for (int t = 0; t < 256; t++)
        {
            weightBack += hist[t];
            if (weightBack == 0)
            {
                continue;
            }
            long weightFore = total - weightBack;
            if (weightFore == 0)
            {
                break;
            }
            sumBack += t * hist[t];
            double meanBack = (double)sumBack / weightBack, meanFore = (double)(sumAll - sumBack) / weightFore;
            double between = (double)weightBack * weightFore * (meanBack - meanFore) * (meanBack - meanFore);
            if (between > best)
            {
                best = between;
                threshold = t;
            }
        }
        return threshold;
    }

    /// <summary>Độ sáng (0–255), phóng <paramref name="scale"/> lần nội suy song tuyến (nét chữ mượt, không vỡ bậc).</summary>
    private static unsafe byte[] ScaledGray(SKBitmap bitmap, int scale, out int w, out int h)
    {
        int sw = bitmap.Width, sh = bitmap.Height;
        var src = new byte[sw * sh];
        byte* basePtr = (byte*)bitmap.GetPixels();
        for (int y = 0; y < sh; y++)
        {
            uint* row = (uint*)(basePtr + (long)y * bitmap.RowBytes);
            for (int x = 0; x < sw; x++)
            {
                uint p = row[x];
                // Pixel trong suốt (premul = 0) coi là nền trắng.
                src[y * sw + x] = (p >> 24) == 0 ? (byte)255 : (byte)OcrText.Luma(p);
            }
        }
        w = sw * scale;
        h = sh * scale;
        if (scale == 1)
        {
            return src;
        }
        var dst = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            float fy = (y + 0.5f) / scale - 0.5f;
            int y0 = Math.Clamp((int)MathF.Floor(fy), 0, sh - 1), y1 = Math.Min(y0 + 1, sh - 1);
            float ty = Math.Clamp(fy - y0, 0, 1);
            for (int x = 0; x < w; x++)
            {
                float fx = (x + 0.5f) / scale - 0.5f;
                int x0 = Math.Clamp((int)MathF.Floor(fx), 0, sw - 1), x1 = Math.Min(x0 + 1, sw - 1);
                float tx = Math.Clamp(fx - x0, 0, 1);
                float top = src[y0 * sw + x0] * (1 - tx) + src[y0 * sw + x1] * tx;
                float bottom = src[y1 * sw + x0] * (1 - tx) + src[y1 * sw + x1] * tx;
                dst[y * w + x] = (byte)(top * (1 - ty) + bottom * ty + 0.5f);
            }
        }
        return dst;
    }

    /// <summary>Xoá (tô nền) các đoạn mực liền nằm ngang ≥ <paramref name="minH"/> và dọc ≥ <paramref name="minV"/>.
    /// Tìm cả 2 chiều trên ảnh gốc rồi mới xoá - xoá ngang trước sẽ cắt rời viền dọc.</summary>
    private static void RemoveLines(byte[] bin, int w, int h, int minH, int minV)
    {
        var kill = new bool[bin.Length];
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w;)
            {
                if (bin[row + x] != 0)
                {
                    x++;
                    continue;
                }
                int end = x;
                while (end < w && bin[row + end] == 0)
                {
                    end++;
                }
                if (end - x >= minH)
                {
                    for (int k = x; k < end; k++)
                    {
                        kill[row + k] = true;
                    }
                }
                x = end;
            }
        }
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h;)
            {
                if (bin[y * w + x] != 0)
                {
                    y++;
                    continue;
                }
                int end = y;
                while (end < h && bin[end * w + x] == 0)
                {
                    end++;
                }
                if (end - y >= minV)
                {
                    for (int k = y; k < end; k++)
                    {
                        kill[k * w + x] = true;
                    }
                }
                y = end;
            }
        }
        for (int i = 0; i < bin.Length; i++)
        {
            if (kill[i])
            {
                bin[i] = 255;
            }
        }
    }

    /// <summary>Các cụm chữ (toạ độ ảnh đã phóng): nối các nét cách nhau ≤ ~0,6 ký tự theo chiều ngang thành 1 cụm.
    /// 1 cụm ≈ 1 nhãn / 1 giá trị trong form - đọc từng cụm riêng (Tesseract) tránh chia dòng sai trên cả trang.</summary>
    public static List<SKRectI> TextBlobs(BinaryImage image)
    {
        int w = image.Width, h = image.Height, s = image.Scale;
        int gapX = 6 * s, gapY = s;
        // Giãn nở theo hàng rồi theo cột (tách được 2 chiều) - nhanh hơn duyệt cả ô vuông quanh từng pixel.
        var rowDilated = new bool[w * h];
        for (int y = 0; y < h; y++)
        {
            int row = y * w, last = int.MinValue;
            for (int x = 0; x < w; x++)
            {
                if (image.Pixels[row + x] == 0)
                {
                    if (x - last <= 2 * gapX + 1)
                    {
                        for (int k = Math.Max(0, last + 1); k < x; k++)
                        {
                            rowDilated[row + k] = true;
                        }
                    }
                    rowDilated[row + x] = true;
                    last = x;
                }
            }
        }
        var dilated = new bool[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!rowDilated[y * w + x])
                {
                    continue;
                }
                for (int yy = Math.Max(0, y - gapY); yy <= Math.Min(h - 1, y + gapY); yy++)
                {
                    dilated[yy * w + x] = true;
                }
            }
        }

        var seen = new bool[w * h];
        var blobs = new List<SKRectI>();
        var queue = new Queue<int>();
        for (int start = 0; start < dilated.Length; start++)
        {
            if (!dilated[start] || seen[start])
            {
                continue;
            }
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            seen[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int cx = c % w, cy = c / w;
                if (image.Pixels[c] == 0)
                {
                    minX = Math.Min(minX, cx);
                    maxX = Math.Max(maxX, cx);
                    minY = Math.Min(minY, cy);
                    maxY = Math.Max(maxY, cy);
                }
                if (cx > 0) Visit(c - 1);
                if (cx < w - 1) Visit(c + 1);
                if (cy > 0) Visit(c - w);
                if (cy < h - 1) Visit(c + w);
            }
            if (maxX < 0)
            {
                continue;
            }
            int bh = maxY - minY + 1;
            // Quá thấp = chấm / gạch rời; quá cao = hình vẽ, icon lớn - không phải 1 dòng chữ.
            if (bh < 5 * s || bh > 40 * s)
            {
                continue;
            }
            blobs.Add(new SKRectI(minX, minY, maxX + 1, maxY + 1));
        }
        return blobs.OrderBy(b => b.Top / (8 * s)).ThenBy(b => b.Left).ToList();

        void Visit(int n)
        {
            if (dilated[n] && !seen[n])
            {
                seen[n] = true;
                queue.Enqueue(n);
            }
        }
    }

    /// <summary>Cắt 1 vùng (toạ độ ảnh đã phóng) kèm lề nền trắng <paramref name="pad"/> px - OCR đọc chữ sát mép kém.</summary>
    public static byte[] Crop(BinaryImage image, SKRectI area, int pad, out int width, out int height)
    {
        width = area.Width + 2 * pad;
        height = area.Height + 2 * pad;
        var crop = new byte[width * height];
        Array.Fill(crop, (byte)255);
        for (int y = 0; y < area.Height; y++)
        {
            int sy = area.Top + y;
            if (sy < 0 || sy >= image.Height)
            {
                continue;
            }
            for (int x = 0; x < area.Width; x++)
            {
                int sx = area.Left + x;
                if (sx >= 0 && sx < image.Width)
                {
                    crop[(y + pad) * width + x + pad] = image.Pixels[sy * image.Width + sx];
                }
            }
        }
        return crop;
    }
}
