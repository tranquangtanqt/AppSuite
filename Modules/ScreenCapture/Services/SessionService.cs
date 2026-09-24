using System.Runtime.CompilerServices;
using System.Text.Json;
using ScreenCapture.Models;
using SkiaSharp;

namespace ScreenCapture.Services;

/// <summary>1 tab ảnh trong Editor, dạng dữ liệu thuần để ghi/đọc phiên làm việc.</summary>
public sealed record SessionDocument(Guid Id, string Title, SKBitmap Bitmap, IReadOnlyList<AnnotationShape> Shapes, bool SavedToFile);

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
    public const int MaxTabs = 30;
    public const long MaxBytes = 300L * 1024 * 1024;
    private const string ManifestName = "session.json";

    public static string Folder { get; } = Path.Combine(Path.GetTempPath(), "AppSuite", "ScreenCapture", "Session");

    /// <summary>Bitmap nào đã ghi ra file nào - bỏ qua encode PNG lại cho ảnh không đổi (bitmap trong
    /// app là bất biến: mọi thao tác sửa pixel đều tạo bitmap mới).</summary>
    private readonly ConditionalWeakTable<SKBitmap, string> _written = new();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public (List<SessionDocument> Documents, Guid? ActiveId) Load()
    {
        var documents = new List<SessionDocument>();
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
                documents.Add(new SessionDocument(tab.Id, tab.Title, bitmap, shapes, tab.SavedToFile));
            }
            return (documents, manifest?.ActiveId);
        }
        catch
        {
            // session.json hỏng → coi như không có phiên cũ; lần Save sau sẽ ghi đè và dọn file thừa.
            return ([], null);
        }
    }

    public void Save(IReadOnlyList<SessionDocument> documents, Guid? activeId)
    {
        Directory.CreateDirectory(Folder);

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
                Image = WriteBitmap(doc.Bitmap, doc.Id),
            };
            files.Add(dto.Image);
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
        var referenced = kept.SelectMany(t => t.Shapes.Select(s => s.Image).Append(t.Image))
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
            Image = imageFile,
        };
        switch (shape)
        {
            case LineArrowAnnotation line:
                dto.IsArrow = line.IsArrow;
                break;
            case TextAnnotation text:
                dto.Text = text.Text;
                dto.FontSize = text.FontSize;
                break;
            case StampAnnotation stamp:
                dto.StampKind = stamp.Kind.ToString();
                dto.NumberValue = stamp.NumberValue;
                dto.OutlineColor = (uint)stamp.OutlineColor;
                break;
        }
        return dto;
    }

    private AnnotationShape? FromDto(ShapeDto dto)
    {
        AnnotationShape? shape = dto.Type switch
        {
            nameof(RectangleAnnotation) => new RectangleAnnotation(),
            nameof(EllipseAnnotation) => new EllipseAnnotation(),
            nameof(HighlightAnnotation) => new HighlightAnnotation(),
            nameof(LineArrowAnnotation) => new LineArrowAnnotation { IsArrow = dto.IsArrow ?? true },
            nameof(TextAnnotation) => new TextAnnotation { Text = dto.Text ?? string.Empty, FontSize = dto.FontSize ?? 20f },
            nameof(StampAnnotation) when Enum.TryParse<StampKind>(dto.StampKind, out var kind) => new StampAnnotation
            {
                Kind = kind,
                NumberValue = dto.NumberValue ?? 0,
                OutlineColor = new SKColor(dto.OutlineColor ?? (uint)SKColors.White),
            },
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
        return shape;
    }

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
        public string Image { get; set; } = string.Empty;
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
        public bool? IsArrow { get; set; }
        public string? Text { get; set; }
        public float? FontSize { get; set; }
        public string? StampKind { get; set; }
        public int? NumberValue { get; set; }
        public uint? OutlineColor { get; set; }
        public string? Image { get; set; }
    }
}
