using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ScreenCapture.Models;

/// <summary>Mẫu tên file khi tự lưu ảnh chụp (Cài đặt > Lưu ảnh), vd <c>{date}_{time}_{app}</c> →
/// <c>2026-10-07_10-31-10_EXCEL.png</c>. Dấu <c>\</c> (hoặc <c>/</c>) trong mẫu = thư mục con, vd <c>{date}\{time}</c>.
///
/// Thẻ: {date} yyyy-MM-dd, {time} HH-mm-ss (ghép "{date}_{time}" = DateTime.ToString("yyyy-MM-dd_HH-mm-ss")), {app} tên
/// chương trình của cửa sổ bị chụp (tiến trình: EXCEL, msedge...), {window} tiêu đề cửa sổ (cắt ngắn), {mode} kiểu chụp,
/// {size} rộng x cao, {n} số thứ tự 001, 002... (lớn nhất đang có trong thư mục đích + 1). Ký tự cấm trong tên file → "_".
/// Thẻ lạ giữ nguyên; mẫu rỗng / ra tên rỗng → mẫu mặc định.</summary>
public static class FileNameTemplate
{
    public const string Default = "{date}_{time}_{app}";

    /// <summary>Mẫu chọn nhanh trong Cài đặt (ô vẫn gõ được mẫu tuỳ ý).</summary>
    public static readonly string[] Presets =
    [
        Default,
        "{date}_{time}",
        "{date}_{window}_{n}",
        "{app}_{window}_{time}",
        @"{date}\{date}_{time}_{app}",
        @"{date}\{time}_{app}",
    ];

    /// <summary>Giải thích các thẻ - hiện dưới ô mẫu trong Cài đặt và trong F1.</summary>
    public const string TokenHelp =
        "{date} ngày (2026-10-07) · {time} giờ (10-31-10) · {app} chương trình (EXCEL, msedge) · {window} tiêu đề cửa sổ · " +
        "{mode} kiểu chụp · {size} rộng x cao · {n} số thứ tự 001, 002… · dấu \\ = thư mục con";

    private const int MaxWindowLength = 50;
    private const int MaxSegmentLength = 150;

    /// <summary>Đường dẫn tương đối (có thể có thư mục con, chưa có đuôi .png) tính từ <paramref name="rootFolder"/>.
    /// <paramref name="rootFolder"/> null = không dò thư mục ({n} = 001) - dùng cho xem trước.</summary>
    public static string Resolve(string? template, CaptureInfo info, int width, int height, string? rootFolder)
    {
        var segments = Split(string.IsNullOrWhiteSpace(template) ? Default : template);
        var resolved = segments.Select(s => Fill(s, info, width, height, n: null)).ToList();
        if (resolved.Count == 0 || resolved[^1].Length == 0)
        {
            segments = Split(Default);
            resolved = segments.Select(s => Fill(s, info, width, height, n: null)).ToList();
        }
        if (segments.Any(s => s.Contains("{n}", StringComparison.OrdinalIgnoreCase)))
        {
            var directories = resolved.Take(resolved.Count - 1).Select(d => d.Replace("{n}", string.Empty, StringComparison.OrdinalIgnoreCase));
            string? folder = rootFolder is null ? null : Path.Combine([rootFolder, .. directories]);
            int n = NextNumber(folder, segments[^1], info, width, height);
            resolved = segments.Select(s => Fill(s, info, width, height, n)).ToList();
        }
        return Path.Combine([.. resolved]);
    }

    /// <summary>Tên mẫu cho xem trước trong Cài đặt: chụp Excel lúc này, ảnh 1920 x 1080.</summary>
    public static string Preview(string? template) =>
        Resolve(template, new CaptureInfo(DateTime.Now, CaptureMode.Region, "EXCEL", "Báo cáo tháng 10.xlsx - Excel"), 1920, 1080, null) + ".png";

    /// <summary>Đuôi ảnh gõ kèm ở cuối mẫu ("{date}_{app}.png") bị bỏ - tự lưu luôn là PNG và tự thêm ".png".</summary>
    private static readonly Regex ImageExtension = new(@"\.(png|jpe?g|bmp|gif|webp)\s*$", RegexOptions.IgnoreCase);

    private static List<string> Split(string template) =>
        ImageExtension.Replace(template.Trim(), string.Empty)
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s is not ("." or ".."))
            .ToList();

    /// <summary>Thay thẻ trong 1 đoạn (tên thư mục / tên file) rồi bỏ ký tự cấm. <paramref name="n"/> null = giữ "{n}".</summary>
    private static string Fill(string segment, CaptureInfo info, int width, int height, int? n)
    {
        string app = info.App.Length > 0 ? info.App : "Desktop";
        string window = info.Window.Length > 0 ? info.Window : app;
        if (window.Length > MaxWindowLength)
        {
            window = window[..MaxWindowLength].TrimEnd();
        }
        const StringComparison Ignore = StringComparison.OrdinalIgnoreCase;
        string text = segment
            .Replace("{date}", info.Time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Ignore)
            .Replace("{time}", info.Time.ToString("HH-mm-ss", CultureInfo.InvariantCulture), Ignore)
            .Replace("{app}", app, Ignore)
            .Replace("{window}", window, Ignore)
            .Replace("{mode}", ModeName(info.Mode), Ignore)
            .Replace("{size}", $"{width}x{height}", Ignore);
        if (n is { } number)
        {
            text = text.Replace("{n}", number.ToString("D3"), Ignore);
        }
        return Sanitize(text);
    }

    /// <summary>{n} kế tiếp: số lớn nhất của các file .png trong thư mục khớp đúng mẫu tên (kể cả bản " (2)") + 1.</summary>
    private static int NextNumber(string? folder, string fileSegment, CaptureInfo info, int width, int height)
    {
        if (folder is null || !Directory.Exists(folder))
        {
            return 1;
        }
        // Thay {n} bằng 1 dấu riêng trước khi bỏ ký tự cấm, rồi dựng regex từ phần còn lại.
        const string Marker = "\u0001N\u0001";
        string withMarker = Fill(fileSegment.Replace("{n}", Marker, StringComparison.OrdinalIgnoreCase), info, width, height, null);
        var parts = withMarker.Split(Marker);
        var pattern = new Regex("^" + string.Join(@"(\d+)", parts.Select(Regex.Escape)) + @"( \(\d+\))?\.png$", RegexOptions.IgnoreCase);
        int max = 0;
        foreach (var file in Directory.EnumerateFiles(folder, "*.png"))
        {
            var match = pattern.Match(Path.GetFileName(file));
            if (match.Success && int.TryParse(match.Groups[1].Value, out int value))
            {
                max = Math.Max(max, value);
            }
        }
        return max + 1;
    }

    public static string ModeName(CaptureMode mode) => mode switch
    {
        CaptureMode.FullScreen => "ToanManHinh",
        CaptureMode.Monitor => "ManHinh",
        CaptureMode.Window => "CuaSo",
        CaptureMode.Region => "Vung",
        CaptureMode.FixedRegion => "VungCoDinh",
        CaptureMode.Scroll => "Cuon",
        _ => mode.ToString(),
    };

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Ký tự cấm (\ / : * ? " &lt; &gt; | và ký tự điều khiển) → "_", gộp khoảng trắng, bỏ dấu chấm / khoảng trắng
    /// cuối, tránh tên dành riêng của Windows (CON, NUL...), cắt ≤ 150 ký tự. Giữ dấu \u0001 (dấu {n} khi dò số).</summary>
    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            sb.Append(c != '\u0001' && (invalid.Contains(c) || char.IsControl(c)) ? '_' : c);
        }
        string result = Regex.Replace(sb.ToString(), @"\s+", " ").Trim().TrimEnd('.', ' ');
        if (result.Length > MaxSegmentLength)
        {
            result = result[..MaxSegmentLength].TrimEnd('.', ' ');
        }
        if (Reserved.Contains(result.Split('.')[0]))
        {
            result = "_" + result;
        }
        return result;
    }
}
