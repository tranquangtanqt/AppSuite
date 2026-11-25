using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using DbTools.Models;
using DbTools.Services;
using Microsoft.Extensions.Logging;

namespace DbTools.ViewModels;

/// <summary>Dữ liệu dùng chung giữa các trang (module không có DI container, như FileTools): hồ sơ kết nối + cài đặt.
/// Sửa hồ sơ nào là tự lưu ngay.</summary>
public static class AppState
{
    private static readonly ConnectionStore ConnectionStore = new();
    private static readonly SettingsStore SettingsStore = new();

    public static HistoryStore History { get; } = new();

    public static ObservableCollection<ConnectionProfile> Profiles { get; } = [];

    public static AppSettings Settings { get; } = SettingsStore.Load();

    static AppState()
    {
        Pg.ConnectTimeoutSeconds = Settings.ConnectTimeoutSeconds;
        foreach (var p in ConnectionStore.Load())
        {
            Watch(p);
            Profiles.Add(p);
        }
        Profiles.CollectionChanged += (_, e) =>
        {
            foreach (var p in e.NewItems?.OfType<ConnectionProfile>() ?? [])
            {
                Watch(p);
            }
            foreach (var p in e.OldItems?.OfType<ConnectionProfile>() ?? [])
            {
                p.PropertyChanged -= Profile_PropertyChanged;
            }
            if (e.Action != NotifyCollectionChangedAction.Move)
            {
                SaveProfiles();
            }
        };
    }

    private static void Watch(ConnectionProfile p) => p.PropertyChanged += Profile_PropertyChanged;

    private static void Profile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ConnectionProfile.Target) or nameof(ConnectionProfile.Display)
            or nameof(ConnectionProfile.PickerText) or nameof(ConnectionProfile.TreeLabel)))
        {
            SaveProfiles();
        }
    }

    /// <summary>Hồ sơ dùng lần trước (theo mã; cài đặt cũ chỉ có tên), không còn thì hồ sơ đầu.</summary>
    public static ConnectionProfile? LastProfile =>
        (Settings.LastProfileId.Length > 0
            ? Profiles.FirstOrDefault(p => p.Id == Settings.LastProfileId)
            : Profiles.FirstOrDefault(p => p.Name == Settings.LastProfile))
        ?? Profiles.FirstOrDefault();

    public static void SaveProfiles()
    {
        try
        {
            ConnectionStore.Save(Profiles);
        }
        catch (Exception ex)
        {
            AppLog.For("Connections").LogError(ex, "Không lưu được hồ sơ kết nối");
        }
    }

    public static void SaveSettings()
    {
        try
        {
            SettingsStore.Save(Settings);
        }
        catch (Exception ex)
        {
            AppLog.For("Settings").LogError(ex, "Không lưu được cài đặt");
        }
    }
}
