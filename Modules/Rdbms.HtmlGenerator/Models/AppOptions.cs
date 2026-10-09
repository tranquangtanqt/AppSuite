namespace Rdbms.HtmlGenerator.Models;

/// <summary>Values from the "Cai dat" dialog, persisted in Data\Config\config.xml.</summary>
public sealed class AppOptions
{
    public const int DefaultConnectTimeoutSeconds = 10;
    public const int DefaultCommandTimeoutSeconds = 120;

    /// <summary>Host không tới được (sai IP, VPN chưa bật, firewall chặn) thì báo lỗi sau ngần này giây.</summary>
    public int ConnectTimeoutSeconds { get; set; } = DefaultConnectTimeoutSeconds;

    /// <summary>Giới hạn cho mỗi câu truy vấn đọc schema (database rất lớn / máy chủ chậm thì tăng lên).</summary>
    public int CommandTimeoutSeconds { get; set; } = DefaultCommandTimeoutSeconds;

    /// <summary>Xuất HTML xong thì tự mở file trong trình duyệt.</summary>
    public bool OpenHtmlAfterExport { get; set; }

    /// <summary>Xuất HTML xong thì mở thư mục chứa file (Explorer, chọn sẵn file vừa xuất).</summary>
    public bool OpenFolderAfterExport { get; set; }

    /// <summary>Clamped copies so a hand-edited config.xml (0, số âm, quá lớn) can't hang or break the import.</summary>
    public int EffectiveConnectTimeoutSeconds => System.Math.Clamp(ConnectTimeoutSeconds, 1, 600);

    public int EffectiveCommandTimeoutSeconds => System.Math.Clamp(CommandTimeoutSeconds, 1, 3600);
}
