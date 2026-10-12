namespace MeRon.Core.Data;

public enum ColumnKind
{
    Integer,
    Text,
}

public sealed record ColumnDef(string Name, ColumnKind Kind);

/// <summary>1 bảng nghiệp vụ. <paramref name="FilePrefix"/>: tên file backup của app Android là
/// <c>&lt;tên bảng viết thường&gt;_&lt;yyyyMMdd_HHmmss&gt;.tsv</c>.</summary>
public sealed record TableDef(string Name, string Title, string PrimaryKey, IReadOnlyList<ColumnDef> Columns, string CreateSql)
{
    public string FilePrefix => Name.ToLowerInvariant();

    public ColumnDef? Find(string column) =>
        Columns.FirstOrDefault(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Schema giống hệt app Android me-ron (lib/core/services/database/database_config.dart, version 2): cùng tên bảng,
/// tên cột, kiểu - nhờ vậy file TSV backup của điện thoại nạp thẳng 1-1, và sau này xuất ngược lại cũng không cần đổi.
/// Ngày giờ lưu dạng TEXT đúng như chuỗi gốc (điện thoại ghi lẫn "2026-06-27T14:20:53.242992" và "2026-06-27 07:20:53");
/// hàm date() của SQLite hiểu cả hai.
/// </summary>
public static class Schema
{
    public const int Version = 2;

    private static ColumnDef I(string name) => new(name, ColumnKind.Integer);
    private static ColumnDef T(string name) => new(name, ColumnKind.Text);

    public static readonly TableDef Address = new("Address", "Địa chỉ", "code",
        [T("code"), T("name"), T("createdAt"), T("updatedAt")],
        """
        CREATE TABLE IF NOT EXISTS Address (
            code TEXT NOT NULL,
            name TEXT,
            createdAt DATETIME DEFAULT CURRENT_TIMESTAMP,
            updatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (code)
        );
        """);

    public static readonly TableDef Users = new("Users", "Khách hàng", "id",
        [I("id"), T("name"), T("address"), T("phone"), T("note"), T("createdAt"), T("updatedAt")],
        """
        CREATE TABLE IF NOT EXISTS Users (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT,
            address TEXT,
            phone TEXT,
            note TEXT,
            createdAt DATETIME DEFAULT CURRENT_TIMESTAMP,
            updatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
        );
        """);

    public static readonly TableDef Categories = new("Categories", "Danh mục món ăn", "id",
        [I("id"), T("name"), T("description"), T("createdAt"), T("updatedAt")],
        """
        CREATE TABLE IF NOT EXISTS Categories (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT,
            description TEXT,
            createdAt DATETIME DEFAULT CURRENT_TIMESTAMP,
            updatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
        );
        """);

    public static readonly TableDef Products = new("Products", "Món ăn", "id",
        [I("id"), I("categoryId"), T("name"), T("imageUrl"), I("price"), T("description"), T("createdAt"), T("updatedAt")],
        """
        CREATE TABLE IF NOT EXISTS Products (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            categoryId INTEGER,
            name TEXT,
            imageUrl TEXT,
            price INTEGER,
            description TEXT,
            createdAt DATETIME DEFAULT CURRENT_TIMESTAMP,
            updatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
        );
        """);

    public static readonly TableDef Orders = new("Orders", "Đơn hàng", "id",
        [I("id"), I("userId"), I("status"), T("deliveryDatetime"), T("paymentDatetime"), I("discountValue"), I("subTotal"),
         I("total"), T("note"), T("createdAt"), T("updatedAt")],
        """
        CREATE TABLE IF NOT EXISTS Orders (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            userId INTEGER,
            status INTEGER,
            deliveryDatetime DATETIME DEFAULT CURRENT_TIMESTAMP,
            paymentDatetime DATETIME DEFAULT CURRENT_TIMESTAMP,
            discountValue INTEGER,
            subTotal INTEGER,
            total INTEGER,
            note TEXT,
            createdAt DATETIME DEFAULT CURRENT_TIMESTAMP,
            updatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
        );
        """);

    public static readonly TableDef OrderItems = new("OrderItems", "Chi tiết đơn hàng", "id",
        [I("id"), I("orderId"), I("productId"), T("snapshotName"), I("snapshotPrice"), I("quantity"), I("lineTotal"),
         T("createdAt"), T("updatedAt")],
        """
        CREATE TABLE IF NOT EXISTS OrderItems (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            orderId INTEGER,
            productId INTEGER,
            snapshotName TEXT,
            snapshotPrice INTEGER,
            quantity INTEGER,
            lineTotal INTEGER,
            createdAt DATETIME DEFAULT CURRENT_TIMESTAMP,
            updatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
        );
        """);

    public static readonly TableDef Purchases = new("Purchases", "Phiếu nhập", "id",
        [I("id"), T("date"), I("total"), T("createdAt"), T("updatedAt")],
        """
        CREATE TABLE IF NOT EXISTS Purchases (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            date TEXT DEFAULT (DATE('now')),
            total INTEGER,
            createdAt DATETIME DEFAULT CURRENT_TIMESTAMP,
            updatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
        );
        """);

    public static readonly TableDef PurchaseItems = new("PurchaseItems", "Chi tiết phiếu nhập", "id",
        [I("id"), I("purchaseId"), T("name"), I("price"), T("createdAt"), T("updatedAt")],
        """
        CREATE TABLE IF NOT EXISTS PurchaseItems (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            purchaseId INTEGER,
            name TEXT,
            price INTEGER,
            createdAt DATETIME DEFAULT CURRENT_TIMESTAMP,
            updatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
        );
        """);

    /// <summary>8 bảng có trong backup của điện thoại, đúng thứ tự xuất của BackupService.exportToLocal.</summary>
    public static IReadOnlyList<TableDef> BackupTables { get; } =
        [Address, Users, Categories, Products, Orders, OrderItems, Purchases, PurchaseItems];

    /// <summary>Thông tin riêng của bản máy tính (lần import gần nhất...) - không phải dữ liệu nghiệp vụ, không xuất ra TSV.</summary>
    public const string MetaTableSql = "CREATE TABLE IF NOT EXISTS MeRonMeta (key TEXT PRIMARY KEY, value TEXT);";

    /// <summary>Index cho các truy vấn lọc / báo cáo (điện thoại không có, nhưng không ảnh hưởng dữ liệu).</summary>
    public static readonly string[] IndexSql =
    [
        "CREATE INDEX IF NOT EXISTS IX_Orders_userId ON Orders(userId);",
        "CREATE INDEX IF NOT EXISTS IX_Orders_delivery ON Orders(deliveryDatetime);",
        "CREATE INDEX IF NOT EXISTS IX_OrderItems_orderId ON OrderItems(orderId);",
        "CREATE INDEX IF NOT EXISTS IX_OrderItems_productId ON OrderItems(productId);",
        "CREATE INDEX IF NOT EXISTS IX_Products_categoryId ON Products(categoryId);",
        "CREATE INDEX IF NOT EXISTS IX_PurchaseItems_purchaseId ON PurchaseItems(purchaseId);",
    ];
}
