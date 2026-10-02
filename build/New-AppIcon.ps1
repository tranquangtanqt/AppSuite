<#
.SYNOPSIS
  Vẽ icon app (.ico nhiều cỡ 16–256 px): ký hiệu trắng (glyph Segoe Fluent Icons hoặc chữ) trên nền màu bo góc.
.EXAMPLE
  .\build\New-AppIcon.ps1 -OutPath Modules\CsvEditor\Assets\CsvEditor.ico -Glyph E80A -Color 16A34A
  .\build\New-AppIcon.ps1 -OutPath Modules\ModuleA\Assets\ModuleA.ico -Text A -Color 64748B
.NOTES
  Modules\Directory.Build.props tự dùng Assets\<Tên project>.ico làm icon exe + chép cạnh exe; cửa sổ gán bằng
  SharedUI.Helpers.WindowIcon.Apply. Cỡ ≤ 128 ghi dạng DIB (GDI+ / System.Drawing không đọc được mục PNG), 256 ghi PNG.
  Cần font Segoe Fluent Icons (Windows 11).
#>
param([string]$OutPath, [string]$Glyph = "E722", [string]$Text = "", [string]$Color = "D86445")
$OutPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutPath) # đường dẫn tương đối theo thư mục hiện tại của PowerShell
Add-Type -AssemblyName System.Drawing
$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$pngs = @()
foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.Clear([System.Drawing.Color]::Transparent)
    $r = $size * 0.22; $d = $r * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90); $path.AddArc(0, $size - $d, $d, $d, 90, 90); $path.CloseFigure()
    $rgb = [Convert]::ToInt32($Color, 16)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(($rgb -shr 16) -band 255, ($rgb -shr 8) -band 255, $rgb -band 255))), $path)
    $font = if ($Text) { New-Object System.Drawing.Font 'Segoe UI Semibold', ($size * 0.6), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel) } else { New-Object System.Drawing.Font 'Segoe Fluent Icons', ($size * 0.62), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel) }
    $fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $s = if ($Text) { $Text } else { [string][char][Convert]::ToInt32($Glyph, 16) }
    $g.DrawString($s, $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, 0, $size, $size), $fmt)
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    if ($size -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # DIB 32 bpp: BITMAPINFOHEADER (cao x2 = ảnh + mặt nạ AND), điểm ảnh BGRA từ dưới lên, mặt nạ AND toàn 0.
        $bw = New-Object IO.BinaryWriter $ms
        $bw.Write([uint32]40); $bw.Write([int32]$size); $bw.Write([int32]($size * 2)); $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]0); $bw.Write([uint32]0); $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
        for ($y = $size - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $size; $x++) { $c = $bmp.GetPixel($x, $y); $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A) }
        }
        $maskRow = [int]([math]::Ceiling($size / 32) * 4)
        $bw.Write((New-Object byte[] ($maskRow * $size)))
        $bw.Flush()
    }
    $bmp.Dispose()
    $pngs += , $ms.ToArray()
}
$out = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
[IO.File]::WriteAllBytes($OutPath, $out.ToArray())
"wrote $OutPath ($($out.Length) bytes)"
