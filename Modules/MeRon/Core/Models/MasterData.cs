namespace MeRon.Core.Models;

/// <summary>Địa chỉ giao hàng (toà nhà / khu vực). Khoá = mã do người dùng đặt (W1, W2...).</summary>
public sealed record Address(string Code, string? Name);

/// <summary>Danh mục món ăn.</summary>
public sealed record Category(long Id, string? Name, string? Description);

/// <summary>Khách hàng (bảng Users của điện thoại - không phải tài khoản đăng nhập). <see cref="Address"/> là chữ tự do.</summary>
public sealed record Customer(long Id, string? Name, string? Address, string? Phone, string? Note);

/// <summary>Món ăn. <see cref="ImageUrl"/> giữ nguyên khi sửa (bản máy tính không quản lý ảnh).</summary>
public sealed record Product(long Id, long? CategoryId, string? Name, string? ImageUrl, long? Price, string? Description);
