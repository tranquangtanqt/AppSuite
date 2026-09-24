using SkiaSharp;

namespace ScreenCapture.Commands;

/// <summary>Destructive pixel-level bucket fill (like MS Paint / PicPick's Fill tool) - NOT an
/// annotation shape, mutates the base bitmap directly. Undo restores the pre-fill bitmap, same
/// old/new-bitmap-snapshot approach as CropCommand.</summary>
public sealed class FloodFillCommand : IEditCommand
{
    private readonly Action<SKBitmap> _setBitmap;
    private readonly SKBitmap _oldBitmap;
    private readonly SKBitmap _newBitmap;

    public FloodFillCommand(Action<SKBitmap> setBitmap, SKBitmap oldBitmap, SKPointI seed, SKColor fillColor, byte tolerance = 30)
    {
        _setBitmap = setBitmap;
        _oldBitmap = oldBitmap;
        _newBitmap = oldBitmap.Copy();
        FloodFill(_newBitmap, seed.X, seed.Y, fillColor, tolerance);
    }

    public string Description => "Tô màu tràn";
    public bool ChangesStructure => false;

    public void Execute() => _setBitmap(_newBitmap);
    public void Undo() => _setBitmap(_oldBitmap);

    /// <summary>Iterative scanline flood fill directly on the bitmap's raw Bgra8888 pixel buffer -
    /// avoids GetPixel/SetPixel (too slow for multi-megapixel captures) and avoids recursion (stack
    /// overflow risk on large fill regions).</summary>
    private static unsafe void FloodFill(SKBitmap bitmap, int seedX, int seedY, SKColor fillColor, byte tolerance)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        if (seedX < 0 || seedX >= width || seedY < 0 || seedY >= height)
        {
            return;
        }

        byte* pixels = (byte*)bitmap.GetPixels().ToPointer();
        int stride = bitmap.RowBytes;

        byte* Pixel(int x, int y) => pixels + y * stride + x * 4; // B,G,R,A order (Bgra8888)

        byte* seedPixel = Pixel(seedX, seedY);
        byte targetB = seedPixel[0], targetG = seedPixel[1], targetR = seedPixel[2], targetA = seedPixel[3];
        byte fillB = fillColor.Blue, fillG = fillColor.Green, fillR = fillColor.Red, fillA = fillColor.Alpha;

        if (Math.Abs(targetB - fillB) <= tolerance && Math.Abs(targetG - fillG) <= tolerance &&
            Math.Abs(targetR - fillR) <= tolerance && Math.Abs(targetA - fillA) <= tolerance)
        {
            return; // already the target color, nothing to do
        }

        bool Matches(int x, int y)
        {
            byte* p = Pixel(x, y);
            return Math.Abs(p[0] - targetB) <= tolerance && Math.Abs(p[1] - targetG) <= tolerance &&
                   Math.Abs(p[2] - targetR) <= tolerance && Math.Abs(p[3] - targetA) <= tolerance;
        }

        void Fill(int x, int y)
        {
            byte* p = Pixel(x, y);
            p[0] = fillB; p[1] = fillG; p[2] = fillR; p[3] = fillA;
        }

        var visited = new bool[width * height];
        var stack = new Stack<(int X, int Y)>();
        stack.Push((seedX, seedY));

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if (x < 0 || x >= width || y < 0 || y >= height || visited[y * width + x] || !Matches(x, y))
            {
                continue;
            }

            // Find the horizontal span on this row that matches, starting from x.
            int left = x;
            while (left - 1 >= 0 && !visited[y * width + (left - 1)] && Matches(left - 1, y))
            {
                left--;
            }
            int right = x;
            while (right + 1 < width && !visited[y * width + (right + 1)] && Matches(right + 1, y))
            {
                right++;
            }

            for (int px = left; px <= right; px++)
            {
                Fill(px, y);
                visited[y * width + px] = true;

                if (y > 0 && !visited[(y - 1) * width + px] && Matches(px, y - 1))
                {
                    stack.Push((px, y - 1));
                }
                if (y < height - 1 && !visited[(y + 1) * width + px] && Matches(px, y + 1))
                {
                    stack.Push((px, y + 1));
                }
            }
        }
    }
}
