using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DbTools.Models;

/// <summary>1 hồ sơ kết nối PostgreSQL có tên (vd "dev local", "test 192.168.x.x"). Mật khẩu chỉ nằm trong bộ nhớ; file
/// lưu bản đã mã hoá (<see cref="ProtectedPassword"/>, DPAPI theo tài khoản Windows).</summary>
public sealed partial class ConnectionProfile : ObservableObject
{
    /// <summary>Mã riêng của hồ sơ (ConnectionStore cấp) - nhớ "hồ sơ lần trước" theo mã, vì 2 thư mục có thể có hồ sơ cùng tên.</summary>
    public string Id { get; set; } = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>Thư mục trong cây hồ sơ, vd "202604_MCF7/eci"; rỗng = ở gốc (<see cref="Core.ProfileTree"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PickerText))]
    private string _folder = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Target))]
    private string _host = "localhost";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Target))]
    private int _port = 5432;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Target))]
    private string _database = string.Empty;

    [ObservableProperty]
    private string _username = "postgres";

    [ObservableProperty]
    [property: JsonIgnore]
    private string _password = string.Empty;

    /// <summary>Schema chạy script (chọn ở trang Chạy, nhớ theo hồ sơ). Rỗng = theo search_path mặc định của server / user
    /// (thường là public); có giá trị = mở kết nối xong đặt search_path = schema đó.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SchemaOrNull), nameof(SchemaText))]
    private string _schema = string.Empty;

    /// <summary>Khi đã chọn schema: tìm cả trong public (search_path = schema, public) - để script dùng được bảng / hàm nền
    /// nằm ở public. Đối tượng mới vẫn tạo ở schema đã chọn.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SchemaText))]
    private bool _includePublic = true;

    /// <summary>Schema đã chọn (bỏ khoảng trắng), null = mặc định.</summary>
    [JsonIgnore]
    public string? SchemaOrNull => string.IsNullOrWhiteSpace(Schema) ? null : Schema.Trim();

    /// <summary>Mô tả ngắn để hiện / ghi lịch sử, vd "schema abc + public" hoặc "schema mặc định".</summary>
    [JsonIgnore]
    public string SchemaText => SchemaOrNull is { } s
        ? $"schema {s}{(IncludePublic && s != "public" ? " + public" : string.Empty)}"
        : "schema mặc định (search_path)";

    /// <summary>Hồ sơ quan trọng (Production): tô đỏ, Chạy / Restore / Copy / Ngắt phiên phải gõ lại tên DB (<see cref="Core.ProductionGuard"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PickerText))]
    private bool _isProduction;

    /// <summary>Chữ trong ô chọn server (ComboBox) ở các trang: kèm thư mục; hồ sơ Production có dấu đỏ ở đầu. Tên đứng trước
    /// thư mục: ô hẹp cắt đuôi, cắt mất tên thì không phân biệt được 2 DB cùng thư mục.</summary>
    [JsonIgnore]
    public string PickerText => (IsProduction ? "🔴 PRODUCTION · " : string.Empty)
        + (Folder.Length == 0 ? Display
            : string.IsNullOrWhiteSpace(Name) ? $"{Target}  ‹{FolderText}›"
            : $"{Name}  ‹{FolderText}›  ({Target})");

    /// <summary>Tên kèm thư mục, vd "202604_MCF7 / eci / dev" (hộp hỏi lại, lịch sử).</summary>
    [JsonIgnore]
    public string FullName => Name.Length == 0 || Folder.Length == 0 ? Name : $"{FolderText} / {Name}";

    /// <summary>Chữ 1 dòng trong cây hồ sơ.</summary>
    [JsonIgnore]
    public string TreeLabel => string.IsNullOrWhiteSpace(Name) ? Target : Name;

    private string FolderText => Folder.Replace("/", " / ");

    /// <summary>Mật khẩu đã mã hoá (Base64) - chỉ dùng khi đọc / ghi file.</summary>
    public string ProtectedPassword { get; set; } = string.Empty;

    /// <summary>host:port/database - hiện trong danh sách và hộp thoại hỏi lại trước khi chạy.</summary>
    [JsonIgnore]
    public string Target => $"{Host}:{Port}/{Database}";

    [JsonIgnore]
    public string Display => string.IsNullOrWhiteSpace(Name) ? Target : $"{Name}  ({Target})";

    partial void OnNameChanged(string value) => DisplayChanged();

    partial void OnHostChanged(string value) => DisplayChanged();

    partial void OnPortChanged(int value) => DisplayChanged();

    partial void OnDatabaseChanged(string value) => DisplayChanged();

    private void DisplayChanged()
    {
        OnPropertyChanged(nameof(Display));
        OnPropertyChanged(nameof(PickerText));
        OnPropertyChanged(nameof(TreeLabel));
    }

    public ConnectionProfile Clone(string name) => new()
    {
        Name = name,
        Folder = Folder,
        Host = Host,
        Port = Port,
        Database = Database,
        Username = Username,
        Password = Password,
        Schema = Schema,
        IncludePublic = IncludePublic,
        IsProduction = IsProduction,
    };

    public override string ToString() => PickerText;
}
