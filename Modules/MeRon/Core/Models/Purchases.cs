namespace MeRon.Core.Models;

/// <summary>1 khoản trong phiếu nhập: tên hàng + giá tiền (không có số lượng, như điện thoại).</summary>
public sealed record PurchaseItem(long Id, long PurchaseId, string? Name, long Price);

/// <summary>Phiếu nhập / mua nguyên liệu theo ngày. <see cref="Date"/> giữ nguyên chuỗi trong database.</summary>
public sealed record Purchase(long Id, string? Date, long Total, IReadOnlyList<PurchaseItem> Items)
{
    public DateTime? Day => DbDate.Parse(Date);
}

/// <summary>Form phiếu nhập. <see cref="Id"/> 0 = phiếu mới.</summary>
public sealed record PurchaseDraft(long Id, DateTime Date, IReadOnlyList<PurchaseLineDraft> Lines);

public sealed record PurchaseLineDraft(string? Name, long? Price);
