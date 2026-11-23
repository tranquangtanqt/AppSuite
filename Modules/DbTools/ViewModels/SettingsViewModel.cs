using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DbTools.Services;

namespace DbTools.ViewModels;

/// <summary>
/// Trang Cài đặt: thời gian chờ kết nối (mọi trang, mọi hồ sơ), thư mục dump, thư mục PostgreSQL client, Sao lưu trước khi
/// chạy, thư mục dữ liệu của app. Thư mục dump / client / Sao lưu trước khi chạy dùng chung giá trị với ô ở trang Sao lưu /
/// Chạy (các trang đó đọc lại khi quay về). Sửa là lưu ngay.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public int MinTimeout => AppSettings.MinConnectTimeout;
    public int MaxTimeout => AppSettings.MaxConnectTimeout;

    /// <summary>double cho NumberBox (gõ chữ / để trống → NaN thì bỏ qua).</summary>
    [ObservableProperty]
    private double _connectTimeout;

    [ObservableProperty]
    private string _dumpFolder = string.Empty;

    [ObservableProperty]
    private string _pgBinFolder = string.Empty;

    [ObservableProperty]
    private string _clientInfo = string.Empty;

    [ObservableProperty]
    private bool _backupBeforeRun;

    /// <summary>Nhật ký server: thư mục log của server vượt bao nhiêu MB thì cảnh báo.</summary>
    [ObservableProperty]
    private double _logWarnMb;

    partial void OnLogWarnMbChanged(double value)
    {
        if (_loading || double.IsNaN(value))
        {
            return;
        }
        AppState.Settings.LogWarnMb = (int)Math.Round(value);
        AppState.SaveSettings();
        if (LogWarnMb != AppState.Settings.LogWarnMb)
        {
            LogWarnMb = AppState.Settings.LogWarnMb;
        }
    }

    public string DataFolder => DataPaths.Root;

    private bool _loading;

    /// <summary>Đọc lại từ cài đặt (mỗi lần vào trang - có thể vừa đổi ở trang Sao lưu / Chạy).</summary>
    public void Load()
    {
        _loading = true;
        ConnectTimeout = AppState.Settings.ConnectTimeoutSeconds;
        DumpFolder = AppState.Settings.DumpFolder;
        PgBinFolder = AppState.Settings.PgBinFolder;
        BackupBeforeRun = AppState.Settings.BackupBeforeRun;
        LogWarnMb = AppState.Settings.LogWarnMb;
        _loading = false;
        _ = RefreshClientAsync();
    }

    partial void OnConnectTimeoutChanged(double value)
    {
        if (_loading || double.IsNaN(value))
        {
            return;
        }
        AppState.Settings.ConnectTimeoutSeconds = (int)Math.Round(value);
        Pg.ConnectTimeoutSeconds = AppState.Settings.ConnectTimeoutSeconds;
        AppState.SaveSettings();
        if (ConnectTimeout != AppState.Settings.ConnectTimeoutSeconds)
        {
            ConnectTimeout = AppState.Settings.ConnectTimeoutSeconds; // ngoài 5-120 → kẹp lại
        }
    }

    partial void OnDumpFolderChanged(string value)
    {
        if (_loading || value.Trim().Length == 0)
        {
            return;
        }
        AppState.Settings.DumpFolder = value.Trim();
        AppState.SaveSettings();
    }

    partial void OnPgBinFolderChanged(string value)
    {
        if (_loading)
        {
            return;
        }
        AppState.Settings.PgBinFolder = value.Trim();
        AppState.SaveSettings();
        _ = RefreshClientAsync();
    }

    partial void OnBackupBeforeRunChanged(bool value)
    {
        if (_loading)
        {
            return;
        }
        AppState.Settings.BackupBeforeRun = value;
        AppState.SaveSettings();
    }

    /// <summary>pg_dump / pg_restore / psql sẽ dùng: thư mục đã chọn, trống = bản mới nhất tìm được trên máy.</summary>
    private async Task RefreshClientAsync()
    {
        var folder = PgBinFolder.Trim();
        ClientInfo = "Đang tìm pg_dump / pg_restore...";
        try
        {
            var client = await PgTools.ResolveAsync(folder);
            if (folder != PgBinFolder.Trim())
            {
                return; // đã đổi tiếp trong lúc tìm
            }
            ClientInfo = client is null
                ? (folder.Length == 0 ? "Không tìm thấy PostgreSQL client trên máy - cài PostgreSQL (hoặc chỉ phần Command Line Tools) rồi mở lại."
                    : "Thư mục này không có pg_dump.exe / pg_restore.exe.")
                : $"Dùng: {client.Display}";
        }
        catch (Exception ex)
        {
            ClientInfo = "Không kiểm tra được: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task BrowseDumpFolderAsync()
    {
        if (await Shell.PickFolderAsync() is { } folder)
        {
            DumpFolder = folder;
        }
    }

    [RelayCommand]
    private async Task BrowseClientFolderAsync()
    {
        if (await Shell.PickFolderAsync() is { } folder)
        {
            PgBinFolder = folder;
        }
    }

    [RelayCommand]
    private void AutoClient() => PgBinFolder = string.Empty;

    [RelayCommand]
    private void OpenDumpFolder()
    {
        Directory.CreateDirectory(DumpFolder.Trim());
        Shell.Open(DumpFolder.Trim());
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        Directory.CreateDirectory(DataFolder);
        Shell.Open(DataFolder);
    }

    [RelayCommand]
    private void ResetTimeout() => ConnectTimeout = AppSettings.DefaultConnectTimeout;
}
