using MeRon.Core.Data;
using MeRon.Core.Models;
using MeRon.Core.Orders;

namespace MeRon.Core;

/// <summary>Mức của 1 dòng thông báo: <see cref="Warning"/> = việc đang chờ (tính vào số trên chuông), <see cref="Info"/> = chỉ để biết.</summary>
public enum NoticeLevel
{
    Info,
    Warning,
}

/// <summary>Loại thông báo - nút hành động trên khung Thông báo theo loại.</summary>
public enum NoticeKind
{
    Confirm,
    Unsynced,
    Backup,
}

public sealed record Notice(NoticeKind Kind, NoticeLevel Level, string Title, string Detail);

/// <summary>
/// Chuông "Thông báo" ở chân menu trái (2026-11-16): gom các việc cần để ý - đơn cần xác nhận (<see cref="OrderConfirm"/>), thay đổi
/// chưa xuất sang điện thoại, bản sao lưu gần nhất (luôn hiện; quá <see cref="BackupStaleDays"/> ngày / chưa có bản nào thì thành
/// việc chờ). Số trên chuông = số dòng <see cref="NoticeLevel.Warning"/>.
/// </summary>
public static class Notices
{
    public const int BackupStaleDays = 3;

    public static IReadOnlyList<Notice> Build(IReadOnlyList<Order> confirm, UnsyncedEdits edits, DateTime? lastBackup, bool hasData, DateTime now)
    {
        var list = new List<Notice>();
        if (confirm.Count > 0)
        {
            var unknownCustomer = confirm.Count(o => OrderConfirm.IsUnknownCustomer(o.UserName));
            var noPrice = confirm.Count(o => OrderConfirm.UnknownPriceItems(o).Count > 0);
            var marked = confirm.Count(o => !OrderConfirm.IsUnknownCustomer(o.UserName) && OrderConfirm.Parse(o.Note).Marked);
            var parts = new List<string>();
            if (unknownCustomer > 0)
            {
                parts.Add($"{VnFormat.Number(unknownCustomer)} chưa rõ khách");
            }
            if (noPrice > 0)
            {
                parts.Add($"{VnFormat.Number(noPrice)} chưa có giá");
            }
            if (marked > 0)
            {
                parts.Add($"{VnFormat.Number(marked)} tự đánh dấu");
            }
            list.Add(new Notice(NoticeKind.Confirm, NoticeLevel.Warning, $"{VnFormat.Number(confirm.Count)} đơn cần xác nhận", string.Join(" · ", parts)));
        }
        if (edits.Any)
        {
            list.Add(new Notice(NoticeKind.Unsynced, NoticeLevel.Warning, "Chưa xuất sang điện thoại", edits.ShortText()));
        }
        if (hasData)
        {
            list.Add(BackupNotice(lastBackup, now));
        }
        return list;
    }

    /// <summary>Dòng sao lưu: luôn có; chưa có bản nào / bản gần nhất quá <see cref="BackupStaleDays"/> ngày → việc chờ.</summary>
    public static Notice BackupNotice(DateTime? lastBackup, DateTime now)
    {
        if (lastBackup is not { } last)
        {
            return new Notice(NoticeKind.Backup, NoticeLevel.Warning, "Chưa có bản sao lưu nào",
                "Bật Tự sao lưu mỗi ngày ở Cài đặt → Sao lưu tự động (mở lại MeRon là có bản đầu tiên).");
        }
        var days = (now.Date - last.Date).Days;
        var when = days switch
        {
            0 => $"hôm nay {last:HH:mm}",
            1 => $"hôm qua {last:HH:mm}",
            _ => $"{last:dd/MM/yyyy HH:mm} ({days} ngày trước)",
        };
        return days > BackupStaleDays
            ? new Notice(NoticeKind.Backup, NoticeLevel.Warning, "Lâu chưa sao lưu", $"Bản sao lưu gần nhất: {when}. Kiểm tra Cài đặt → Sao lưu tự động.")
            : new Notice(NoticeKind.Backup, NoticeLevel.Info, "Sao lưu", $"Bản sao lưu gần nhất: {when}.");
    }

    public static int WarningCount(IReadOnlyList<Notice> notices) => notices.Count(n => n.Level == NoticeLevel.Warning);
}
