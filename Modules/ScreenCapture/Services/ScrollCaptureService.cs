using ScreenCapture.Models;
using ScreenCapture.Services.Interop;
using SkiaSharp;

namespace ScreenCapture.Services;

public enum ScrollStopReason
{
    /// <summary>Cuộn xong mà nội dung không đổi → đã tới cuối.</summary>
    ReachedEnd,
    /// <summary>Người dùng bấm Esc.</summary>
    Cancelled,
    /// <summary>Chạm giới hạn số lần cuộn / chiều cao ảnh.</summary>
    LimitReached,
    /// <summary>Không tìm được phần trùng giữa 2 khung (cuộn quá xa, nội dung động...) → dừng ở khung trước.</summary>
    StitchFailed,
}

public enum ScrollDirection
{
    /// <summary>Cuộn xuống, ghép theo chiều dọc.</summary>
    Vertical,
    /// <summary>Cuộn sang phải, ghép theo chiều ngang.</summary>
    Horizontal,
}

public sealed record ScrollCaptureResult(SKBitmap Image, int Frames, ScrollStopReason Reason);

/// <summary>
/// Chụp cuộn: trong 1 vùng màn hình, giả lập lăn chuột (SendInput, con trỏ đặt giữa vùng), chụp lại vùng
/// sau mỗi lần cuộn và ghép các khung bằng cách dò độ dịch dọc giữa 2 khung liên tiếp.
///
/// Dò độ dịch: băm từng dòng pixel (FNV-1a 64-bit) → với mỗi độ dịch s, đếm số dòng của khung mới khớp
/// với dòng (i + s) của khung trước; chọn s có nhiều dòng "có nội dung" khớp nhất trong số s đạt ≥ 90%
/// dòng khớp. Dòng một màu (nền trống) không tính điểm để tránh khớp nhầm ở vùng trống.
/// Đầu/chân trang cố định trong vùng (dòng giống hệt ở cùng vị trí trong 2 khung) được tách ra: đầu
/// trang giữ 1 lần ở trên cùng, chân trang lấy 1 lần từ khung cuối, chỉ phần giữa được ghép nối.
///
/// Cuộn ngang: mỗi khung được CHUYỂN VỊ (cột ↔ dòng) ngay khi chụp, nên "cuộn sang phải, cột mới hiện
/// ở mép phải" thành "cuộn xuống, dòng mới hiện ở dưới" → dùng lại nguyên thuật toán dọc ở trên, ảnh
/// ghép xong chuyển vị ngược lại. Lăn ngang bằng MOUSEEVENTF_HWHEEL; app không phản ứng (khung không
/// đổi ở bước đầu) thì chuyển sang Shift + lăn dọc (cách trình duyệt / Excel cuộn ngang).
/// </summary>
public sealed class ScrollCaptureService
{
    private const double MinMatchRatio = 0.9;

    private readonly ICaptureService _capture;

    /// <summary>Giới hạn số lần cuộn / độ dài ảnh (theo chiều cuộn) và thời gian chờ sau mỗi lần lăn -
    /// lấy từ Cài đặt > Chụp cuộn (xem <see cref="AppSettings"/>), giá trị lạ bị kẹp về khoảng hợp lệ.</summary>
    public int MaxSteps { get; }
    public int MaxLength { get; }
    private readonly int _settleMs; // chờ cuộn mượt (smooth scrolling) của trình duyệt dừng hẳn

    public ScrollCaptureService(ICaptureService capture, AppSettings settings)
    {
        _capture = capture;
        MaxSteps = Math.Clamp(settings.ScrollMaxSteps, AppSettings.ScrollMaxStepsRange.Min, AppSettings.ScrollMaxStepsRange.Max);
        MaxLength = Math.Clamp(settings.ScrollMaxLength, AppSettings.ScrollMaxLengthRange.Min, AppSettings.ScrollMaxLengthRange.Max);
        _settleMs = Math.Clamp(settings.ScrollSettleMs, AppSettings.ScrollSettleMsRange.Min, AppSettings.ScrollSettleMsRange.Max);
    }

    private bool _horizontal;
    private bool _useShiftWheel;
    private int _notches;

    public async Task<ScrollCaptureResult> CaptureAsync(RECT rect, ScrollDirection direction = ScrollDirection.Vertical)
    {
        _horizontal = direction == ScrollDirection.Horizontal;
        _useShiftWheel = false;
        int length = _horizontal ? rect.Right - rect.Left : rect.Bottom - rect.Top;
        // Cuộn khoảng 1/3 kích thước vùng theo hướng cuộn mỗi bước (1 nấc chuột ~100px ở trình duyệt) →
        // 2 khung liên tiếp luôn trùng nhau nhiều, dễ ghép.
        _notches = Math.Clamp(length / 300, 1, 5);

        NativeMethods.GetCursorPos(out var originalCursor);
        var center = new POINT { X = (rect.Left + rect.Right) / 2, Y = (rect.Top + rect.Bottom) / 2 };
        NativeMethods.SetCursorPos(center.X, center.Y);

        // Đưa cửa sổ nằm dưới vùng chọn lên foreground: khi gọi từ khay hệ thống / phím tắt, foreground
        // đang là taskbar hoặc launcher đang ẩn - lăn chuột giả lập sẽ đi nhầm chỗ nếu Windows tắt
        // "Scroll inactive windows when I hover over them".
        var target = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(center), NativeMethods.GA_ROOT);
        if (target != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(target);
        }
        NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE); // xoá trạng thái "đã bấm" cũ

        // Khung đầu phải chụp khi cửa sổ đã vẽ lại xong sau khi được kích hoạt (dòng đang chọn đổi màu,
        // khung focus...) - nếu không khung 2 khác khung 1 ở chỗ không do cuộn và không ghép được (lỗi
        // lúc được lúc không đã gặp khi test). Chụp tới khi 2 lần liên tiếp giống hệt nhau.
        var (first, previousRows) = await CaptureStableAsync(rect);
        var previous = first;
        int topStatic = -1, bottomStatic = 0;
        SKBitmap? firstBody = null; // khung đầu bỏ chân trang - cắt khi đã biết chân trang cao bao nhiêu
        var strips = new List<SKBitmap>();
        int totalHeight = 0;
        int frames = 1;
        var reason = ScrollStopReason.LimitReached;

        try
        {
            for (int step = 0; step < MaxSteps; step++)
            {
                if (EscapePressed())
                {
                    reason = ScrollStopReason.Cancelled;
                    break;
                }

                Scroll();
                await Task.Delay(_settleMs);
                if (EscapePressed())
                {
                    reason = ScrollStopReason.Cancelled;
                    break;
                }

                var current = CaptureFrame(rect);
                var currentRows = RowInfo.Of(current);
                DumpFrame(step == 0 ? previous : null, current, step);

                // Khung không đổi: có thể đã tới cuối, NHƯNG cũng có thể 1 lần lăn chuột bị lỡ (đã gặp khi
                // test: dừng "tới cuối" khi mới được nửa trang). Lăn thử lại tối đa 2 lần mới kết luận.
                for (int retry = 0; retry < 2 && previousRows.Hashes.AsSpan().SequenceEqual(currentRows.Hashes); retry++)
                {
                    current.Dispose();
                    if (_horizontal && step == 0 && !_useShiftWheel)
                    {
                        // Lăn ngang lần đầu không có tác dụng → app không hỗ trợ HWHEEL, thử Shift + lăn dọc.
                        _useShiftWheel = true;
                    }
                    Scroll();
                    await Task.Delay(_settleMs);
                    current = CaptureFrame(rect);
                    currentRows = RowInfo.Of(current);
                }
                if (previousRows.Hashes.AsSpan().SequenceEqual(currentRows.Hashes))
                {
                    current.Dispose();
                    reason = ScrollStopReason.ReachedEnd;
                    break;
                }

                // So khớp chỉ trên các cột có nội dung cuộn - bỏ viền / khung focus / thanh cuộn / lề đứng
                // yên theo cửa sổ (đã gặp khi test: 2 cột mép trái khác nhau làm hỏng hash của MỌI dòng).
                var (maskedPrev, maskedCur) = MaskedRows(previous, current);
                if (topStatic < 0)
                {
                    (topStatic, bottomStatic) = FindStaticBands(maskedPrev, maskedCur);
                    firstBody = Crop(first, 0, first.Height - bottomStatic);
                }

                int shift = FindShift(maskedPrev, maskedCur, topStatic, bottomStatic);
                if (shift <= 0)
                {
                    // Có thể còn đang cuộn mượt / vẽ lại → chờ thêm, chụp lại khung này rồi thử 1 lần nữa.
                    current.Dispose();
                    await Task.Delay(_settleMs);
                    current = CaptureFrame(rect);
                    currentRows = RowInfo.Of(current);
                    (maskedPrev, maskedCur) = MaskedRows(previous, current);
                    shift = FindShift(maskedPrev, maskedCur, topStatic, bottomStatic);
                }
                if (shift <= 0)
                {
                    current.Dispose();
                    reason = ScrollStopReason.StitchFailed;
                    break;
                }

                // Dòng mới xuất hiện ở khung này = `shift` dòng cuối của phần giữa (trên chân trang).
                int stripBottom = current.Height - bottomStatic;
                strips.Add(Crop(current, stripBottom - shift, stripBottom));
                totalHeight += shift;
                frames++;

                if (!ReferenceEquals(previous, first))
                {
                    previous.Dispose();
                }
                previous = current;
                previousRows = currentRows;

                if (previous.Height + totalHeight >= MaxLength) // cuộn ngang: "Height" là chiều rộng (khung đã chuyển vị)
                {
                    reason = ScrollStopReason.LimitReached;
                    break;
                }
            }

            var image = firstBody is null || strips.Count == 0
                ? first.Copy() // chưa ghép được gì (1 khung) → trả nguyên khung đầu
                : Compose(firstBody, strips, previous, bottomStatic);
            if (_horizontal)
            {
                var upright = Transpose(image); // về lại hướng thật (ghép theo chiều ngang)
                image.Dispose();
                image = upright;
            }
            return new ScrollCaptureResult(image, frames, reason);
        }
        finally
        {
            foreach (var strip in strips)
            {
                strip.Dispose();
            }
            firstBody?.Dispose();
            if (!ReferenceEquals(previous, first))
            {
                previous.Dispose();
            }
            first.Dispose();
            NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y);
        }
    }

    /// <summary>Ảnh cuối = thân khung đầu (đã gồm đầu trang, bỏ chân trang) + các dải mới theo thứ tự +
    /// chân trang lấy 1 lần từ khung cuối.</summary>
    private static SKBitmap Compose(SKBitmap firstBody, List<SKBitmap> strips, SKBitmap last, int bottomStatic)
    {
        int width = firstBody.Width;
        int total = firstBody.Height + strips.Sum(s => s.Height) + bottomStatic;
        var result = new SKBitmap(new SKImageInfo(width, total, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(result);
        int y = 0;
        canvas.DrawBitmap(firstBody, 0, y);
        y += firstBody.Height;
        foreach (var strip in strips)
        {
            canvas.DrawBitmap(strip, 0, y);
            y += strip.Height;
        }
        if (bottomStatic > 0)
        {
            canvas.DrawBitmap(last, SKRect.Create(0, last.Height - bottomStatic, width, bottomStatic),
                SKRect.Create(0, y, width, bottomStatic));
        }
        return result;
    }

    private async Task<(SKBitmap Frame, RowInfo Rows)> CaptureStableAsync(RECT rect)
    {
        await Task.Delay(200);
        var frame = CaptureFrame(rect);
        var rows = RowInfo.Of(frame);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            await Task.Delay(120);
            var again = CaptureFrame(rect);
            var againRows = RowInfo.Of(again);
            bool stable = rows.Hashes.AsSpan().SequenceEqual(againRows.Hashes);
            frame.Dispose();
            frame = again;
            rows = againRows;
            if (stable)
            {
                break;
            }
        }
        return (frame, rows);
    }

    /// <summary>Chẩn đoán: đặt biến môi trường SCREENCAPTURE_SCROLL_DEBUG_DIR thì lưu từng khung chụp
    /// (frame_000 = khung đầu) để xem vì sao không ghép được. Không đặt = không làm gì.</summary>
    private static void DumpFrame(SKBitmap? first, SKBitmap frame, int step)
    {
        if (Environment.GetEnvironmentVariable("SCREENCAPTURE_SCROLL_DEBUG_DIR") is not { Length: > 0 } dir)
        {
            return;
        }
        Directory.CreateDirectory(dir);
        static void Save(SKBitmap bitmap, string path)
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(path);
            data.SaveTo(stream);
        }
        if (first is not null)
        {
            Save(first, Path.Combine(dir, "frame_000.png"));
        }
        Save(frame, Path.Combine(dir, $"frame_{step + 1:D3}.png"));
    }

    /// <summary>Chụp vùng; cuộn ngang thì chuyển vị ngay để phần ghép chỉ cần xử lý chiều dọc.</summary>
    private SKBitmap CaptureFrame(RECT rect)
    {
        var frame = _capture.CaptureRect(rect);
        if (!_horizontal)
        {
            return frame;
        }
        var transposed = Transpose(frame);
        frame.Dispose();
        return transposed;
    }

    /// <summary>Lăn chuột 1 bước theo hướng đang chụp: dọc = lăn xuống; ngang = lăn ngang sang phải
    /// (HWHEEL), hoặc Shift + lăn xuống nếu app không hỗ trợ lăn ngang.</summary>
    private void Scroll()
    {
        bool horizontalWheel = _horizontal && !_useShiftWheel;
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            mi = new NativeMethods.MOUSEINPUT
            {
                // WHEEL: âm = xuống. HWHEEL: dương = sang phải.
                mouseData = unchecked((uint)((horizontalWheel ? 1 : -1) * NativeMethods.WHEEL_DELTA * _notches)),
                dwFlags = horizontalWheel ? NativeMethods.MOUSEEVENTF_HWHEEL : NativeMethods.MOUSEEVENTF_WHEEL,
            },
        };
        bool holdShift = _horizontal && _useShiftWheel;
        if (holdShift)
        {
            NativeMethods.keybd_event(NativeMethods.VK_SHIFT, 0, 0, 0);
        }
        unsafe
        {
            NativeMethods.SendInput(1, &input, sizeof(NativeMethods.INPUT));
        }
        if (holdShift)
        {
            NativeMethods.keybd_event(NativeMethods.VK_SHIFT, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
        }
    }

    /// <summary>Chuyển vị ảnh: pixel (x, y) → (y, x). Tự nghịch đảo (chuyển vị 2 lần = ảnh gốc).</summary>
    private static unsafe SKBitmap Transpose(SKBitmap source)
    {
        int w = source.Width, h = source.Height;
        var result = new SKBitmap(new SKImageInfo(h, w, SKColorType.Bgra8888, SKAlphaType.Premul));
        byte* src = (byte*)source.GetPixels();
        byte* dst = (byte*)result.GetPixels();
        int srcRow = source.RowBytes, dstRow = result.RowBytes;
        for (int y = 0; y < h; y++)
        {
            uint* s = (uint*)(src + (long)y * srcRow);
            for (int x = 0; x < w; x++)
            {
                *(uint*)(dst + (long)x * dstRow + y * 4L) = s[x];
            }
        }
        return result;
    }

    private static bool EscapePressed() => (NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8001) != 0;

    private static SKBitmap Crop(SKBitmap source, int top, int bottom)
    {
        var strip = new SKBitmap(new SKImageInfo(source.Width, bottom - top, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(strip);
        canvas.DrawBitmap(source, SKRect.Create(0, top, source.Width, bottom - top), SKRect.Create(0, 0, source.Width, bottom - top));
        return strip;
    }

    /// <summary>Đầu/chân trang cố định: số dòng liên tiếp từ trên / từ dưới giống hệt ở cùng vị trí trong 2
    /// khung. Nếu 2 dải chiếm quá nửa vùng thì coi như không có (tránh nhầm khi nội dung ít thay đổi).</summary>
    private static (int Top, int Bottom) FindStaticBands(RowInfo a, RowInfo b)
    {
        int h = a.Hashes.Length;
        int top = 0;
        while (top < h && a.Hashes[top] == b.Hashes[top])
        {
            top++;
        }
        int bottom = 0;
        while (bottom < h - top && a.Hashes[h - 1 - bottom] == b.Hashes[h - 1 - bottom])
        {
            bottom++;
        }
        return top + bottom > h / 2 ? (0, 0) : (top, bottom);
    }

    private static int FindShift(RowInfo prev, RowInfo cur, int top, int bottom)
    {
        int middle = prev.Hashes.Length - top - bottom;
        int bestShift = -1, bestScore = -1;
        for (int s = 1; s < middle; s++)
        {
            int overlap = middle - s;
            int matches = 0, informative = 0;
            for (int i = 0; i < overlap; i++)
            {
                int curRow = top + i;
                if (RowsMatch(cur, curRow, prev, curRow + s))
                {
                    matches++;
                    if (!cur.Uniform[curRow])
                    {
                        informative++;
                    }
                }
            }
            if (matches >= overlap * MinMatchRatio && informative > bestScore)
            {
                bestScore = informative;
                bestShift = s;
            }
        }
        if (Environment.GetEnvironmentVariable("SCREENCAPTURE_SCROLL_DEBUG_DIR") is { Length: > 0 } debugDir)
        {
            // Chẩn đoán: 5 độ dịch có tỉ lệ dòng khớp cao nhất.
            var top5 = Enumerable.Range(1, Math.Max(0, middle - 1))
                .Select(s =>
                {
                    int ov = middle - s, m = 0;
                    for (int i = 0; i < ov; i++)
                    {
                        if (RowsMatch(cur, top + i, prev, top + i + s)) m++;
                    }
                    return (s, ratio: (double)m / ov);
                })
                .OrderByDescending(t => t.ratio).Take(5);
            File.AppendAllText(Path.Combine(debugDir, "shift.log"),
                $"top={top} bottom={bottom} middle={middle} best={bestShift} score={bestScore} candidates={string.Join(", ", top5.Select(t => $"{t.s}:{t.ratio:P0}"))}{Environment.NewLine}");
        }
        // Chỉ khớp được toàn dòng trống thì không đủ tin cậy.
        return bestScore > 0 ? bestShift : -1;
    }

    private const int BlockCount = 16;
    private const int MinMatchingBlocks = 12;

    /// <summary>2 dòng coi là khớp khi ≥ 12/16 khối ngang giống nhau - chịu được 1 phần nhỏ mép dòng thay
    /// đổi mà không do cuộn (con trượt thanh cuộn di chuyển, đã gặp khi test: 90% dòng lệch vì vùng chọn
    /// lấn vào thanh cuộn).</summary>
    private static bool RowsMatch(RowInfo a, int rowA, RowInfo b, int rowB)
    {
        if (a.Hashes[rowA] == b.Hashes[rowB])
        {
            return true;
        }
        int equal = 0;
        int offsetA = rowA * BlockCount, offsetB = rowB * BlockCount;
        for (int k = 0; k < BlockCount; k++)
        {
            if (a.Blocks[offsetA + k] == b.Blocks[offsetB + k])
            {
                equal++;
            }
        }
        return equal >= MinMatchingBlocks;
    }

    /// <summary>Hash từng dòng của 2 khung, chỉ tính các cột "động" - cột có ít nhất 1 pixel khác nhau
    /// giữa 2 khung ở cùng vị trí. Cột giống hệt nhau ở cùng vị trí trong cả 2 khung là phần đứng yên
    /// theo cửa sổ (viền, khung focus, thanh cuộn, lề trống) - giữ lại sẽ làm dòng đúng không khớp.</summary>
    private static unsafe (RowInfo Prev, RowInfo Cur) MaskedRows(SKBitmap prev, SKBitmap cur)
    {
        int w = Math.Min(prev.Width, cur.Width), h = Math.Min(prev.Height, cur.Height);
        var dynamicColumns = new bool[w];
        byte* a = (byte*)prev.GetPixels(), b = (byte*)cur.GetPixels();
        for (int y = 0; y < h; y++)
        {
            uint* rowA = (uint*)(a + (long)y * prev.RowBytes), rowB = (uint*)(b + (long)y * cur.RowBytes);
            for (int x = 0; x < w; x++)
            {
                dynamicColumns[x] |= rowA[x] != rowB[x];
            }
        }
        return (RowInfo.Of(prev, dynamicColumns), RowInfo.Of(cur, dynamicColumns));
    }

    /// <summary>Hash cả dòng + hash từng khối ngang (<see cref="BlockCount"/> khối/dòng) + cờ "một màu" của
    /// từng dòng pixel trong 1 khung (tuỳ chọn chỉ trên các cột được chọn).</summary>
    private sealed class RowInfo
    {
        public required ulong[] Hashes { get; init; }
        public required ulong[] Blocks { get; init; } // [dòng * BlockCount + khối]
        public required bool[] Uniform { get; init; }

        public static unsafe RowInfo Of(SKBitmap bitmap, bool[]? columns = null)
        {
            int h = bitmap.Height, w = bitmap.Width;
            // Các cột được tính, chia đều thành BlockCount khối liên tiếp.
            var included = Enumerable.Range(0, w)
                .Where(x => columns is null || (x < columns.Length && columns[x]))
                .ToArray();
            var blockOf = new int[included.Length];
            for (int i = 0; i < included.Length; i++)
            {
                blockOf[i] = (int)((long)i * BlockCount / Math.Max(1, included.Length));
            }

            var hashes = new ulong[h];
            var blocks = new ulong[h * BlockCount];
            var uniform = new bool[h];
            byte* basePtr = (byte*)bitmap.GetPixels();
            int rowBytes = bitmap.RowBytes;
            for (int y = 0; y < h; y++)
            {
                uint* row = (uint*)(basePtr + (long)y * rowBytes);
                ulong hash = 14695981039346656037UL;
                int blockBase = y * BlockCount;
                for (int k = 0; k < BlockCount; k++)
                {
                    blocks[blockBase + k] = 14695981039346656037UL;
                }
                bool same = true;
                uint first = included.Length > 0 ? row[included[0]] : 0;
                for (int i = 0; i < included.Length; i++)
                {
                    uint px = row[included[i]];
                    hash = (hash ^ px) * 1099511628211UL;
                    ref ulong block = ref blocks[blockBase + blockOf[i]];
                    block = (block ^ px) * 1099511628211UL;
                    same &= px == first;
                }
                hashes[y] = hash;
                uniform[y] = same;
            }
            return new RowInfo { Hashes = hashes, Blocks = blocks, Uniform = uniform };
        }
    }
}
