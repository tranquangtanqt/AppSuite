using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ScreenCapture.Models;
using SkiaSharp;

namespace ScreenCapture.Services;

/// <summary>1 tab ảnh trong Editor, dạng dữ liệu thuần để ghi/đọc phiên làm việc.</summary>
public sealed record SessionDocument(Guid Id, string Title, SKBitmap Bitmap, IReadOnlyList<AnnotationShape> Shapes, bool SavedToFile,
    SessionCropSource? CropSource = null, string? FilePath = null);

/// <summary>Ảnh gốc trước lần Cắt / đổi khung đầu tiên của tab + vị trí góc trên-trái của nó theo toạ độ
/// ảnh hiện tại - lưu qua phiên để mở lại app vẫn kéo khung ra lấy lại được phần đã cắt.</summary>
public sealed record SessionCropSource(SKBitmap Original, int OffsetX, int OffsetY);

/// <summary>
/// Nhớ các tab ảnh của Editor qua lần tắt/mở app (giống PicPick), lưu tạm ở
/// <c>%TEMP%\AppSuite\ScreenCapture\Session\</c>:
/// - <c>session.json</c>: danh sách tab + mô tả từng shape (vẫn chỉnh sửa được khi mở lại).
/// - <c>{tabId}_{guid}.png</c>: ảnh nền của tab và các ảnh dán (ImageAnnotation) của tab đó.
///
/// Chống đầy ổ: thư mục chỉ chứa đúng các tab đang mở ở lần lưu gần nhất - mỗi lần <see cref="Save"/>
/// xoá mọi file không còn được session.json tham chiếu (tab đã đóng, phiên cũ). Thêm giới hạn
/// <see cref="MaxTabs"/> tab / <see cref="MaxBytes"/>: vượt thì bỏ tab cũ nhất khỏi phiên lưu tạm
/// (tab vẫn còn mở trong lần chạy hiện tại). Lịch sử Undo không được lưu.
/// </summary>
public sealed class SessionService
{
    /// <summary>Tắt trong Cài đặt → Load trả rỗng, Save chỉ dọn sạch thư mục (không giữ ảnh nào).</summary>
    public bool Enabled { get; set; } = true;
    public int MaxTabs { get; set; } = 30;
    public long MaxBytes { get; set; } = 300L * 1024 * 1024;
    private const string ManifestName = "session.json";

    public void ApplySettings(AppSettings settings)
    {
        Enabled = settings.RememberTabs;
        MaxTabs = Math.Max(1, settings.SessionMaxTabs);
        MaxBytes = Math.Max(1, settings.SessionMaxMegabytes) * 1024L * 1024;
    }

    /// <summary>Dung lượng thư mục phiên hiện tại (hiện ở trang Cài đặt).</summary>
    public static long CurrentSizeBytes() =>
        Directory.Exists(Folder) ? Directory.EnumerateFiles(Folder).Sum(f => new FileInfo(f).Length) : 0;

    /// <summary>Biến môi trường <c>SCREENCAPTURE_SESSION_DIR</c> đổi sang thư mục khác - dùng khi chạy
    /// thử/tự động hoá để KHÔNG đụng vào các tab thật của người dùng trong thư mục mặc định.</summary>
    public static string Folder { get; } =
        Environment.GetEnvironmentVariable("SCREENCAPTURE_SESSION_DIR") is { Length: > 0 } overrideDir
            ? overrideDir
            : Path.Combine(Path.GetTempPath(), "AppSuite", "ScreenCapture", "Session");

    /// <summary>Bitmap nào đã ghi ra file nào - bỏ qua encode PNG lại cho ảnh không đổi (bitmap trong
    /// app là bất biến: mọi thao tác sửa pixel đều tạo bitmap mới).</summary>
    private readonly ConditionalWeakTable<SKBitmap, string> _written = new();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public (List<SessionDocument> Documents, Guid? ActiveId) Load()
    {
        var documents = new List<SessionDocument>();
        if (!Enabled)
        {
            return (documents, null);
        }
        try
        {
            var manifestPath = Path.Combine(Folder, ManifestName);
            if (!File.Exists(manifestPath))
            {
                return (documents, null);
            }

            var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), JsonOptions);
            foreach (var tab in manifest?.Tabs ?? [])
            {
                var imagePath = Path.Combine(Folder, tab.Image);
                var bitmap = File.Exists(imagePath) ? SKBitmap.Decode(imagePath) : null;
                if (bitmap is null)
                {
                    continue; // file ảnh bị xoá (vd Windows dọn %TEMP%) → bỏ tab đó
                }
                _written.AddOrUpdate(bitmap, tab.Image);
                var shapes = tab.Shapes.Select(FromDto).OfType<AnnotationShape>().ToList();
                // Ảnh gốc của phần đã cắt: thiếu file (phiên cũ chưa có trường này, hoặc bị dọn) thì chỉ
                // mất khả năng khôi phục phần đã cắt, tab vẫn mở bình thường.
                var cropPath = tab.CropOriginal is { Length: > 0 } cropFile ? Path.Combine(Folder, cropFile) : null;
                var cropBitmap = cropPath is not null && File.Exists(cropPath) ? SKBitmap.Decode(cropPath) : null;
                SessionCropSource? crop = null;
                if (cropBitmap is not null)
                {
                    _written.AddOrUpdate(cropBitmap, tab.CropOriginal!);
                    crop = new SessionCropSource(cropBitmap, tab.CropOffsetX, tab.CropOffsetY);
                }
                documents.Add(new SessionDocument(tab.Id, tab.Title, bitmap, shapes, tab.SavedToFile, crop, tab.FilePath));
            }
            return (documents, manifest?.ActiveId);
        }
        catch (Exception ex)
        {
            // session.json hỏng → coi như không có phiên cũ; lần Save sau sẽ ghi đè và dọn file thừa.
            AppLog.For(nameof(SessionService)).LogError(ex, "Không đọc được phiên làm việc cũ ({Folder})", Folder);
            return ([], null);
        }
    }

    public void Save(IReadOnlyList<SessionDocument> documents, Guid? activeId)
    {
        Directory.CreateDirectory(Folder);
        if (!Enabled)
        {
            documents = []; // tắt nhớ tab → ghi manifest rỗng, phần dọn file bên dưới xoá hết ảnh cũ
        }

        // Giữ tối đa MaxTabs tab mới nhất (tab mở sau nằm cuối danh sách), rồi cắt tiếp theo dung lượng.
        var candidates = documents.Skip(Math.Max(0, documents.Count - MaxTabs)).Reverse().ToList();
        var kept = new List<TabDto>();
        long totalBytes = 0;
        foreach (var doc in candidates)
        {
            var files = new List<string>();
            var dto = new TabDto
            {
                Id = doc.Id,
                Title = doc.Title,
                SavedToFile = doc.SavedToFile,
                FilePath = doc.FilePath,
                Image = WriteBitmap(doc.Bitmap, doc.Id),
            };
            files.Add(dto.Image);
            if (doc.CropSource is { } crop)
            {
                // Ảnh gốc thường chính là ảnh chụp đã ghi từ trước khi cắt → WriteBitmap dùng lại file cũ.
                dto.CropOriginal = WriteBitmap(crop.Original, doc.Id);
                dto.CropOffsetX = crop.OffsetX;
                dto.CropOffsetY = crop.OffsetY;
                files.Add(dto.CropOriginal);
            }
            foreach (var shape in doc.Shapes)
            {
                string? imageFile = null;
                if (shape is ImageAnnotation image)
                {
                    imageFile = WriteBitmap(image.Image, doc.Id);
                    files.Add(imageFile);
                }
                dto.Shapes.Add(ToDto(shape, imageFile));
            }

            long tabBytes = files.Sum(f => new FileInfo(Path.Combine(Folder, f)).Length);
            if (kept.Count > 0 && totalBytes + tabBytes > MaxBytes)
            {
                break; // luôn giữ ít nhất tab mới nhất
            }
            totalBytes += tabBytes;
            kept.Add(dto);
        }
        kept.Reverse();

        var manifest = new Manifest
        {
            ActiveId = kept.Any(t => t.Id == activeId) ? activeId : kept.LastOrDefault()?.Id,
            Tabs = kept,
        };
        var manifestPath = Path.Combine(Folder, ManifestName);
        var tempPath = manifestPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(manifest, JsonOptions));
        File.Move(tempPath, manifestPath, overwrite: true);

        // Dọn mọi file không còn được tham chiếu: tab đã đóng, tab vượt giới hạn, ảnh của phiên cũ.
        var referenced = kept.SelectMany(t => t.Shapes.Select(s => s.Image).Append(t.Image).Append(t.CropOriginal))
            .OfType<string>().Append(ManifestName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(Folder))
        {
            if (!referenced.Contains(Path.GetFileName(file)))
            {
                try { File.Delete(file); } catch { /* file đang bị khoá - lần Save sau dọn tiếp */ }
            }
        }
    }

    /// <summary>Mỗi bitmap 1 tên file duy nhất ({tabId}_{guid}.png): bitmap đã ghi rồi thì dùng lại file
    /// cũ, không encode lại. File của bitmap không còn dùng (ảnh đã sửa, shape đã xoá) bị dọn ở cuối Save.</summary>
    private string WriteBitmap(SKBitmap bitmap, Guid tabId)
    {
        if (_written.TryGetValue(bitmap, out var existing) && File.Exists(Path.Combine(Folder, existing)))
        {
            return existing;
        }
        var fileName = $"{tabId:N}_{Guid.NewGuid():N}.png";
        var path = Path.Combine(Folder, fileName);
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        using (var stream = File.Create(path))
        {
            data.SaveTo(stream);
        }
        _written.AddOrUpdate(bitmap, fileName);
        return fileName;
    }

    private static ShapeDto ToDto(AnnotationShape shape, string? imageFile)
    {
        var dto = new ShapeDto
        {
            Type = shape.GetType().Name,
            Left = shape.Bounds.Left,
            Top = shape.Bounds.Top,
            Right = shape.Bounds.Right,
            Bottom = shape.Bounds.Bottom,
            Color = (uint)shape.Color,
            StrokeWidth = shape.StrokeWidth,
            FillColor = (uint?)shape.FillColor,
            Dash = shape.Dash == LineDash.Solid ? null : shape.Dash.ToString(),
            Opacity = shape.Opacity >= 1f ? null : shape.Opacity,
            Image = imageFile,
        };
        if (shape is ITextShape textShape)
        {
            dto.Text = textShape.Text;
            dto.FontSize = textShape.FontSize;
            dto.FontFamily = textShape.FontFamily;
            dto.Bold = textShape.Bold ? true : null;
            dto.Italic = textShape.Italic ? true : null;
        }
        switch (shape)
        {
            case RectangleAnnotation rectangle:
                dto.CornerRadius = rectangle.CornerRadius > 0 ? rectangle.CornerRadius : null;
                break;
            case LineArrowAnnotation line:
                dto.StartHead = line.StartHead.ToString();
                dto.EndHead = line.EndHead.ToString();
                break;
            case TextAnnotation text:
                dto.TextBackground = (uint?)text.BackgroundColor;
                dto.TextOutline = (uint?)text.OutlineColor;
                break;
            case CalloutAnnotation callout:
                dto.CornerRadius = callout.CornerRadius;
                dto.TailX = callout.TailOffset.X;
                dto.TailY = callout.TailOffset.Y;
                break;
            case StampAnnotation stamp:
                dto.StampKind = stamp.Kind.ToString();
                dto.NumberValue = stamp.NumberValue;
                dto.OutlineColor = (uint)stamp.OutlineColor;
                break;
            case RedactAnnotation redact:
                dto.RedactMode = redact.Mode.ToString();
                break;
            case FreehandAnnotation freehand:
                // Lưu điểm đã ánh xạ vào Bounds hiện tại (x0, y0, x1, y1...) - lúc nạp lại khung vẽ = Bounds.
                dto.Points = freehand.CurrentPoints().SelectMany(p => new[] { p.X, p.Y }).ToList();
                break;
        }
        return dto;
    }

    private AnnotationShape? FromDto(ShapeDto dto)
    {
        AnnotationShape? shape = dto.Type switch
        {
            nameof(RectangleAnnotation) => new RectangleAnnotation { CornerRadius = dto.CornerRadius ?? 0 },
            nameof(EllipseAnnotation) => new EllipseAnnotation(),
            nameof(HighlightAnnotation) => new HighlightAnnotation(),
            nameof(RedactAnnotation) => new RedactAnnotation
            {
                Mode = Enum.TryParse<RedactMode>(dto.RedactMode, out var mode) ? mode : RedactMode.Mosaic,
            },
            // Phiên cũ chỉ có IsArrow (mũi tên ở điểm cuối hoặc không có đầu nào).
            nameof(LineArrowAnnotation) => new LineArrowAnnotation
            {
                StartHead = ParseEnum(dto.StartHead, ArrowHead.None),
                EndHead = ParseEnum(dto.EndHead, dto.IsArrow ?? true ? ArrowHead.Arrow : ArrowHead.None),
            },
            nameof(TextAnnotation) => new TextAnnotation
            {
                BackgroundColor = ToColor(dto.TextBackground),
                OutlineColor = ToColor(dto.TextOutline),
            },
            nameof(CalloutAnnotation) => new CalloutAnnotation
            {
                CornerRadius = dto.CornerRadius ?? 10f,
                TailOffset = new SKPoint(dto.TailX ?? 0, dto.TailY ?? 0),
            },
            nameof(StampAnnotation) when Enum.TryParse<StampKind>(dto.StampKind, out var kind) => new StampAnnotation
            {
                Kind = kind,
                NumberValue = dto.NumberValue ?? 0,
                OutlineColor = new SKColor(dto.OutlineColor ?? (uint)SKColors.White),
            },
            nameof(FreehandAnnotation) when dto.Points is { Count: >= 2 } => new FreehandAnnotation(),
            nameof(ImageAnnotation) => LoadImageShape(dto.Image),
            _ => null,
        };
        if (shape is null)
        {
            return null;
        }
        // Bounds giữ nguyên thứ tự 4 cạnh (Line/Arrow lưu điểm đầu/cuối, không chuẩn hoá).
        shape.Bounds = new SKRect(dto.Left, dto.Top, dto.Right, dto.Bottom);
        shape.Color = new SKColor(dto.Color);
        shape.StrokeWidth = dto.StrokeWidth;
        shape.FillColor = ToColor(dto.FillColor);
        shape.Dash = ParseEnum(dto.Dash, LineDash.Solid);
        shape.Opacity = Math.Clamp(dto.Opacity ?? 1f, 0.1f, 1f);
        if (shape is ITextShape textShape)
        {
            textShape.Text = dto.Text ?? string.Empty;
            textShape.FontSize = dto.FontSize ?? textShape.FontSize;
            textShape.FontFamily = dto.FontFamily ?? textShape.FontFamily;
            textShape.Bold = dto.Bold ?? false;
            textShape.Italic = dto.Italic ?? false;
        }
        if (shape is TextAnnotation text)
        {
            text.FitBounds(); // phiên cũ lưu khung chữ cố định 200 × 30
        }
        if (shape is FreehandAnnotation freehandShape && dto.Points is { } points)
        {
            freehandShape.SetPoints(Enumerable.Range(0, points.Count / 2).Select(i => new SKPoint(points[2 * i], points[2 * i + 1])));
        }
        return shape;
    }

    private static SKColor? ToColor(uint? value) => value is { } v ? new SKColor(v) : null;

    private static T ParseEnum<T>(string? value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, out var parsed) ? parsed : fallback;

    private ImageAnnotation? LoadImageShape(string? fileName)
    {
        var path = fileName is null ? null : Path.Combine(Folder, fileName);
        var bitmap = path is not null && File.Exists(path) ? SKBitmap.Decode(path) : null;
        if (bitmap is null)
        {
            return null;
        }
        _written.AddOrUpdate(bitmap, fileName!);
        return new ImageAnnotation(bitmap);
    }

    private sealed class Manifest
    {
        public int Version { get; set; } = 1;
        public Guid? ActiveId { get; set; }
        public List<TabDto> Tabs { get; set; } = [];
    }

    private sealed class TabDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public bool SavedToFile { get; set; }
        /// <summary>File gắn với tab (Lưu ghi đè vào đây), null = chưa lưu / phiên cũ.</summary>
        public string? FilePath { get; set; }
        public string Image { get; set; } = string.Empty;
        /// <summary>File ảnh gốc trước khi Cắt (null = tab chưa cắt / đổi khung, hoặc phiên cũ).</summary>
        public string? CropOriginal { get; set; }
        public int CropOffsetX { get; set; }
        public int CropOffsetY { get; set; }
        public List<ShapeDto> Shapes { get; set; } = [];
    }

    private sealed class ShapeDto
    {
        public string Type { get; set; } = string.Empty;
        public float Left { get; set; }
        public float Top { get; set; }
        public float Right { get; set; }
        public float Bottom { get; set; }
        public uint Color { get; set; }
        public float StrokeWidth { get; set; }
        public uint? FillColor { get; set; }
        public string? Dash { get; set; }
        public float? Opacity { get; set; }
        public float? CornerRadius { get; set; }
        /// <summary>Phiên cũ (trước khi có <see cref="StartHead"/> / <see cref="EndHead"/>) - chỉ còn đọc.</summary>
        public bool? IsArrow { get; set; }
        public string? StartHead { get; set; }
        public string? EndHead { get; set; }
        public string? Text { get; set; }
        public float? FontSize { get; set; }
        public string? FontFamily { get; set; }
        public bool? Bold { get; set; }
        public bool? Italic { get; set; }
        public uint? TextBackground { get; set; }
        public uint? TextOutline { get; set; }
        public float? TailX { get; set; }
        public float? TailY { get; set; }
        public string? StampKind { get; set; }
        public int? NumberValue { get; set; }
        public uint? OutlineColor { get; set; }
        public string? RedactMode { get; set; }
        public List<float>? Points { get; set; }
        public string? Image { get; set; }
    }
}
