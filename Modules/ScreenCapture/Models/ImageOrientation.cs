namespace ScreenCapture.Models;

/// <summary>Hướng hiện tại của ảnh so với lúc chụp / mở: ảnh gốc lật ngang (nếu <see cref="Flipped"/>) rồi xoay
/// <see cref="QuarterTurns"/> × 90° theo chiều kim đồng hồ. Mọi chuỗi xoay / lật đều gom được về dạng này, nên "Về hướng
/// ban đầu" chỉ cần tối đa 2 bước biến đổi (xem <see cref="UndoSteps"/>). Đổi cỡ / cắt / đổi khung không đổi hướng.</summary>
public readonly record struct ImageOrientation(int QuarterTurns, bool Flipped)
{
    public static ImageOrientation Original => default;

    public bool IsOriginal => QuarterTurns == 0 && !Flipped;

    /// <summary>Hướng sau khi áp thêm <paramref name="kind"/> lên ảnh đang ở hướng này. Dùng F·R·F = R⁻¹ (lật ngang
    /// làm xoay đổi chiều) và Lật dọc = Lật ngang rồi xoay 180°.</summary>
    public ImageOrientation Then(ImageTransformKind kind) => kind switch
    {
        ImageTransformKind.RotateRight => Normalize(QuarterTurns + 1, Flipped),
        ImageTransformKind.RotateLeft => Normalize(QuarterTurns - 1, Flipped),
        ImageTransformKind.Rotate180 => Normalize(QuarterTurns + 2, Flipped),
        ImageTransformKind.FlipHorizontal => Normalize(-QuarterTurns, !Flipped),
        ImageTransformKind.FlipVertical => Normalize(2 - QuarterTurns, !Flipped),
        _ => this,
    };

    /// <summary>Các bước xoay / lật (theo thứ tự) đưa ảnh từ hướng này về hướng ban đầu.</summary>
    public IReadOnlyList<ImageTransformKind> UndoSteps()
    {
        // Nghịch đảo của R^q·F^f là F^f·R^-q. Chưa lật: xoay ngược q. Đã lật: F·R^-q = R^q·F → lật ngang rồi xoay q
        // (q = 2: lật ngang + xoay 180° = lật dọc, gộp 1 bước).
        if (!Flipped)
        {
            return QuarterTurns switch
            {
                1 => [ImageTransformKind.RotateLeft],
                2 => [ImageTransformKind.Rotate180],
                3 => [ImageTransformKind.RotateRight],
                _ => [],
            };
        }
        return QuarterTurns switch
        {
            1 => [ImageTransformKind.FlipHorizontal, ImageTransformKind.RotateRight],
            2 => [ImageTransformKind.FlipVertical],
            3 => [ImageTransformKind.FlipHorizontal, ImageTransformKind.RotateLeft],
            _ => [ImageTransformKind.FlipHorizontal],
        };
    }

    private static ImageOrientation Normalize(int quarterTurns, bool flipped) => new(((quarterTurns % 4) + 4) % 4, flipped);
}
