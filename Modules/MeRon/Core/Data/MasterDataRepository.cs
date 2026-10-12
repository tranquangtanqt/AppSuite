using MeRon.Core.Models;
using Microsoft.Data.Sqlite;

namespace MeRon.Core.Data;

/// <summary>Lỗi nghiệp vụ hiện nguyên văn cho người dùng (trùng tên, thiếu thông tin, đang được dùng...).</summary>
public sealed class MeRonValidationException(string message) : Exception(message);

/// <summary>
/// Đọc / ghi 4 bảng danh mục: Địa chỉ, Danh mục món ăn, Khách hàng, Món ăn. Quy tắc giống app điện thoại:
/// trùng tên khách hàng (không phân biệt hoa thường) thì không cho lưu; món ăn không xoá được (điện thoại cũng ẩn nút xoá).
/// Thêm ở bản máy tính: không cho xoá khách hàng còn đơn hàng / danh mục còn món - điện thoại không bật khoá ngoại nên
/// xoá sẽ để lại đơn "mồ côi" làm báo cáo sai.
/// </summary>
public sealed class MasterDataRepository(MeRonDatabase database)
{
    // ---- Địa chỉ ----

    public IReadOnlyList<Address> GetAddresses() =>
        Query("SELECT code, name FROM Address ORDER BY code", r => new Address(r.GetString(0), Str(r, 1)));

    public void AddAddress(Address address)
    {
        var code = Required(address.Code, "Mã");
        using var connection = database.Open();
        if (Scalar(connection, "SELECT COUNT(*) FROM Address WHERE code = $code COLLATE NOCASE", ("$code", code)) > 0)
        {
            throw new MeRonValidationException($"Mã địa chỉ \"{code}\" đã tồn tại.");
        }
        var now = MeRonDatabase.Now();
        Execute(connection, "INSERT INTO Address(code, name, createdAt, updatedAt) VALUES($code, $name, $now, $now)",
            ("$code", code), ("$name", Trim(address.Name)), ("$now", now));
    }

    /// <summary>Mã là khoá nên không đổi được khi sửa (giống điện thoại) - chỉ sửa Tên.</summary>
    public void UpdateAddress(Address address)
    {
        using var connection = database.Open();
        Execute(connection, "UPDATE Address SET name = $name, updatedAt = $now WHERE code = $code",
            ("$code", address.Code), ("$name", Trim(address.Name)), ("$now", MeRonDatabase.Now()));
    }

    public void DeleteAddress(string code)
    {
        using var connection = database.Open();
        Execute(connection, "DELETE FROM Address WHERE code = $code", ("$code", code));
    }

    // ---- Danh mục món ăn ----

    public IReadOnlyList<Category> GetCategories() =>
        Query("SELECT id, name, description FROM Categories ORDER BY id", r => new Category(r.GetInt64(0), Str(r, 1), Str(r, 2)));

    public long SaveCategory(Category category)
    {
        var name = Required(category.Name, "Tên");
        using var connection = database.Open();
        var now = MeRonDatabase.Now();
        if (category.Id == 0)
        {
            return Insert(connection, "INSERT INTO Categories(name, description, createdAt, updatedAt) VALUES($name, $description, $now, $now)",
                ("$name", name), ("$description", Trim(category.Description)), ("$now", now));
        }
        Execute(connection, "UPDATE Categories SET name = $name, description = $description, updatedAt = $now WHERE id = $id",
            ("$id", category.Id), ("$name", name), ("$description", Trim(category.Description)), ("$now", now));
        return category.Id;
    }

    public void DeleteCategory(long id)
    {
        using var connection = database.Open();
        var used = Scalar(connection, "SELECT COUNT(*) FROM Products WHERE categoryId = $id", ("$id", id));
        if (used > 0)
        {
            throw new MeRonValidationException($"Danh mục đang có {used} món ăn - chuyển các món sang danh mục khác trước khi xoá.");
        }
        Execute(connection, "DELETE FROM Categories WHERE id = $id", ("$id", id));
    }

    // ---- Khách hàng ----

    public IReadOnlyList<Customer> GetCustomers() =>
        Query("SELECT id, name, address, phone, note FROM Users ORDER BY id",
            r => new Customer(r.GetInt64(0), Str(r, 1), Str(r, 2), Str(r, 3), Str(r, 4)));

    public long SaveCustomer(Customer customer)
    {
        var name = Required(customer.Name, "Tên");
        using var connection = database.Open();

        // Điện thoại so tên bằng toLowerCase - chỉ bỏ qua hoa thường, vẫn phân biệt dấu ("Lan" khác "Lân").
        var lower = name.ToLowerInvariant();
        foreach (var (id, other) in Query(connection, "SELECT id, name FROM Users", r => (r.GetInt64(0), Str(r, 1))))
        {
            if (id != customer.Id && other is not null && other.Trim().ToLowerInvariant() == lower)
            {
                throw new MeRonValidationException($"Tên khách hàng \"{name}\" đã tồn tại.");
            }
        }

        var now = MeRonDatabase.Now();
        if (customer.Id == 0)
        {
            return Insert(connection,
                "INSERT INTO Users(name, address, phone, note, createdAt, updatedAt) VALUES($name, $address, $phone, $note, $now, $now)",
                ("$name", name), ("$address", Trim(customer.Address)), ("$phone", Trim(customer.Phone)), ("$note", Trim(customer.Note)), ("$now", now));
        }
        Execute(connection, "UPDATE Users SET name = $name, address = $address, phone = $phone, note = $note, updatedAt = $now WHERE id = $id",
            ("$id", customer.Id), ("$name", name), ("$address", Trim(customer.Address)), ("$phone", Trim(customer.Phone)),
            ("$note", Trim(customer.Note)), ("$now", now));
        return customer.Id;
    }

    public void DeleteCustomer(long id)
    {
        using var connection = database.Open();
        var orders = Scalar(connection, "SELECT COUNT(*) FROM Orders WHERE userId = $id", ("$id", id));
        if (orders > 0)
        {
            throw new MeRonValidationException($"Khách hàng đang có {orders} đơn hàng - không xoá được.");
        }
        Execute(connection, "DELETE FROM Users WHERE id = $id", ("$id", id));
    }

    // ---- Món ăn ----

    public IReadOnlyList<Product> GetProducts() =>
        Query("SELECT id, categoryId, name, imageUrl, price, description FROM Products ORDER BY id",
            r => new Product(r.GetInt64(0), Long(r, 1), Str(r, 2), Str(r, 3), Long(r, 4), Str(r, 5)));

    public long SaveProduct(Product product)
    {
        var name = Required(product.Name, "Tên món ăn");
        if (product.Price is null or < 0)
        {
            throw new MeRonValidationException("Giá bán phải là số không âm.");
        }
        using var connection = database.Open();
        var now = MeRonDatabase.Now();
        if (product.Id == 0)
        {
            return Insert(connection,
                "INSERT INTO Products(categoryId, name, imageUrl, price, description, createdAt, updatedAt) " +
                "VALUES($categoryId, $name, $imageUrl, $price, $description, $now, $now)",
                ("$categoryId", product.CategoryId), ("$name", name), ("$imageUrl", product.ImageUrl), ("$price", product.Price),
                ("$description", Trim(product.Description)), ("$now", now));
        }
        Execute(connection,
            "UPDATE Products SET categoryId = $categoryId, name = $name, price = $price, description = $description, updatedAt = $now WHERE id = $id",
            ("$id", product.Id), ("$categoryId", product.CategoryId), ("$name", name), ("$price", product.Price),
            ("$description", Trim(product.Description)), ("$now", now));
        return product.Id;
    }

    // ---- tiện ích ----

    private static string Required(string? value, string field) =>
        Trim(value) ?? throw new MeRonValidationException($"Chưa nhập {field}.");

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Str(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i));

    private static long? Long(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetInt64(i);

    private IReadOnlyList<T> Query<T>(string sql, Func<SqliteDataReader, T> map)
    {
        using var connection = database.Open();
        return Query(connection, sql, map);
    }

    private static List<T> Query<T>(SqliteConnection connection, string sql, Func<SqliteDataReader, T> map)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var list = new List<T>();
        while (reader.Read())
        {
            list.Add(map(reader));
        }
        return list;
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql, (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return command;
    }

    private static void Execute(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, sql, parameters);
        command.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, sql, parameters);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static long Insert(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        Execute(connection, sql, parameters);
        return Scalar(connection, "SELECT last_insert_rowid()");
    }
}
