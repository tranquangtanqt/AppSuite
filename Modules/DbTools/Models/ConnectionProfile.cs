using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DbTools.Models;

/// <summary>1 hồ sơ kết nối PostgreSQL có tên (vd "dev local", "test 192.168.x.x"). Mật khẩu chỉ nằm trong bộ nhớ; file
/// lưu bản đã mã hoá (<see cref="ProtectedPassword"/>, DPAPI theo tài khoản Windows).</summary>
public sealed partial class ConnectionProfile : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

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

    /// <summary>Mật khẩu đã mã hoá (Base64) - chỉ dùng khi đọc / ghi file.</summary>
    public string ProtectedPassword { get; set; } = string.Empty;

    /// <summary>host:port/database - hiện trong danh sách và hộp thoại hỏi lại trước khi chạy.</summary>
    [JsonIgnore]
    public string Target => $"{Host}:{Port}/{Database}";

    [JsonIgnore]
    public string Display => string.IsNullOrWhiteSpace(Name) ? Target : $"{Name}  ({Target})";

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Display));

    partial void OnHostChanged(string value) => OnPropertyChanged(nameof(Display));

    partial void OnPortChanged(int value) => OnPropertyChanged(nameof(Display));

    partial void OnDatabaseChanged(string value) => OnPropertyChanged(nameof(Display));

    public ConnectionProfile Clone(string name) => new()
    {
        Name = name,
        Host = Host,
        Port = Port,
        Database = Database,
        Username = Username,
        Password = Password,
    };

    public override string ToString() => Display;
}
