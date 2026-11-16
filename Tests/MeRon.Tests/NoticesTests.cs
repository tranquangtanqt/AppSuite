using MeRon.Core;
using MeRon.Core.Data;
using MeRon.Core.Models;

namespace MeRon.Tests;

/// <summary>Chuông Thông báo ở menu trái (2026-11-16).</summary>
public class NoticesTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 10, 0, 0);

    private static Order O(long id, string user, string? note, long price = 15000) =>
        new(id, id, user, OrderStatus.Shipping, "2026-11-16 00:00:00", null, 0, price, price, note, [new OrderItem(id, id, null, "XX", price, 1, price)]);

    [Fact]
    public void Builds_confirm_unsynced_and_backup_lines()
    {
        var confirm = new[] { O(1, "Chưa rõ khách", "[Cần xác nhận: Chưa rõ khách: Nhung]"), O(2, "Ly Sa", null, 1), O(3, "Ly Sa", "[Cần xác nhận: hỏi số lượng]") };
        var edits = new UnsyncedEdits(46, new DateTime(2026, 11, 3, 14, 48, 0), Now);
        var notices = Notices.Build(confirm, edits, Now.AddHours(-2), hasData: true, Now);

        Assert.Equal([NoticeKind.Confirm, NoticeKind.Unsynced, NoticeKind.Backup], notices.Select(n => n.Kind).ToArray());
        Assert.Equal("3 đơn cần xác nhận", notices[0].Title);
        Assert.Equal("1 chưa rõ khách · 1 chưa có giá · 1 tự đánh dấu", notices[0].Detail);
        Assert.Equal("46 dòng thay đổi chưa xuất (từ 03/11 14:48) · nhập backup sẽ đè mất", notices[1].Detail);
        Assert.Equal(NoticeLevel.Info, notices[2].Level);
        Assert.Equal("Bản sao lưu gần nhất: hôm nay 08:00.", notices[2].Detail);
        Assert.Equal(2, Notices.WarningCount(notices)); // số trên chuông: sao lưu mới thì không tính

        // Không có việc gì: chỉ còn dòng sao lưu (để biết); database trống thì không có dòng nào
        Assert.Equal(0, Notices.WarningCount(Notices.Build([], new UnsyncedEdits(0, null, null), Now, true, Now)));
        Assert.Empty(Notices.Build([], new UnsyncedEdits(0, null, null), null, hasData: false, Now));
    }

    [Fact]
    public void Backup_line_turns_into_a_warning_after_3_days_or_when_missing()
    {
        Assert.Equal("Bản sao lưu gần nhất: hôm qua 21:30.", Notices.BackupNotice(new DateTime(2026, 11, 15, 21, 30, 0), Now).Detail);
        Assert.Equal(NoticeLevel.Info, Notices.BackupNotice(new DateTime(2026, 11, 13, 8, 0, 0), Now).Level); // 3 ngày: còn được
        var stale = Notices.BackupNotice(new DateTime(2026, 11, 12, 8, 0, 0), Now);
        Assert.Equal(NoticeLevel.Warning, stale.Level);
        Assert.Equal("Lâu chưa sao lưu", stale.Title);
        Assert.Contains("12/11/2026 08:00 (4 ngày trước)", stale.Detail);
        Assert.Equal(NoticeLevel.Warning, Notices.BackupNotice(null, Now).Level);
    }
}
