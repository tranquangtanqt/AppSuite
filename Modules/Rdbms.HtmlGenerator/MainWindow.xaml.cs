using System;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rdbms.HtmlGenerator.Models;
using Rdbms.HtmlGenerator.ViewModels;

namespace Rdbms.HtmlGenerator;

/// <summary>
/// Standalone data-dictionary tool for Module E: connects to PostgreSQL or Oracle, saves the schema
/// to SQLite, and exports a browsable static HTML report. Nothing here depends on how this process
/// was started.
/// </summary>
public sealed partial class MainWindow : Window
{
    public RdbmsHtmlGeneratorViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        SharedUI.Helpers.WindowIcon.Apply(this);
        Closed += (_, _) => _helpWindow?.Close(); // đóng app thì đóng luôn cửa sổ Hướng dẫn
        ViewModel.ErrorOccurred += ShowErrorDialog;
        SyncProfileControls(); // hồ sơ + nguồn đã chọn lần trước
    }

    /// <summary>Đặt combobox Hồ sơ / Nguồn theo ViewModel (sau khi đổi / thêm / đổi tên / xoá hồ sơ) - không chạy lại
    /// SelectionChanged của chính nó.</summary>
    private bool _syncingProfile;

    private void SyncProfileControls()
    {
        _syncingProfile = true;
        ProfileComboBox.SelectedItem = ViewModel.CurrentProfile.Name;
        SourceComboBox.SelectedIndex = (int)ViewModel.SelectedSource;
        DeleteProfileItem.IsEnabled = ViewModel.CanDeleteProfile;
        _syncingProfile = false;
    }

    // ----- Hồ sơ kết nối -----

    private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingProfile && ProfileComboBox.SelectedItem is string name)
        {
            ViewModel.SelectProfile(name);
            SyncProfileControls();
        }
    }

    /// <summary>Thêm = true, Đổi tên = false (ProfileNameDialog dùng chung).</summary>
    private bool _addingProfile;

    private async void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        _addingProfile = true;
        ProfileNameDialog.Title = "Thêm hồ sơ kết nối";
        ProfileNameDialog.PrimaryButtonText = "Thêm";
        ProfileNameBox.Text = Services.ConnectionProfiles.SuggestName(ViewModel.Config);
        ProfileNameHint.Text = $"Hồ sơ mới sao chép thông tin kết nối của hồ sơ \"{ViewModel.CurrentProfile.Name}\" - sau đó bấm " +
                               "Thiết lập kết nối để sửa Host / Database... File .db / .html của mỗi hồ sơ tách riêng.";
        await ShowProfileNameDialogAsync();
    }

    private async void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        _addingProfile = false;
        ProfileNameDialog.Title = "Đổi tên hồ sơ";
        ProfileNameDialog.PrimaryButtonText = "Lưu";
        ProfileNameBox.Text = ViewModel.CurrentProfile.Name;
        ProfileNameHint.Text = "Tên file .db / .html đổi theo tên hồ sơ - đọc lại database (bước 1) để có lại dữ liệu.";
        await ShowProfileNameDialogAsync();
    }

    private async System.Threading.Tasks.Task ShowProfileNameDialogAsync()
    {
        ProfileNameError.Visibility = Visibility.Collapsed;
        ProfileNameBox.SelectAll();
        ProfileNameDialog.XamlRoot = Content.XamlRoot;
        await ProfileNameDialog.ShowAsync();
    }

    /// <summary>Tên sai (trống, trùng, ký tự cấm) → báo lỗi dưới ô, giữ hộp thoại mở.</summary>
    private void ProfileNameDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var error = _addingProfile ? ViewModel.AddProfile(ProfileNameBox.Text) : ViewModel.RenameProfile(ProfileNameBox.Text);
        if (error is not null)
        {
            args.Cancel = true;
            ProfileNameError.Text = error;
            ProfileNameError.Visibility = Visibility.Visible;
            ProfileNameBox.Focus(FocusState.Programmatic);
            return;
        }
        SyncProfileControls();
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = ViewModel.CurrentProfile.Name;
        var confirm = new ContentDialog
        {
            Title = "Xoá hồ sơ",
            Content = new TextBlock
            {
                Text = $"Xoá hồ sơ \"{name}\" cùng thông tin kết nối của nó? File .db / .html đã đọc / xuất (trong Data\\Database) vẫn giữ nguyên.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Xoá",
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary && ViewModel.DeleteProfile())
        {
            SyncProfileControls();
        }
    }

    /// <summary>x:Bind helper - enables controls only while no import/export is running.</summary>
    public static bool Not(bool value) => !value;

    /// <summary>x:Bind helper - accent style for the step the user should click next, normal style otherwise.
    /// Uses SharedUI's compact keyed styles: the stock Accent/DefaultButtonStyle would make these 3 buttons
    /// bigger than the rest of the row (they bypass the suite-wide implicit compact Button style).</summary>
    public static Style StepButtonStyle(int nextStep, int step) =>
        (Style)Application.Current.Resources[nextStep == step ? "CompactAccentButtonStyle" : "CompactButtonStyle"];

    private async void ShowErrorDialog(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            CloseButtonText = "Đóng",
            XamlRoot = Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private void SourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingProfile && SourceComboBox.SelectedIndex >= 0)
        {
            ViewModel.SelectedSource = (DatabaseSourceType)SourceComboBox.SelectedIndex;
        }
    }

    private async void OpenSettingsDialog_Click(object sender, RoutedEventArgs e)
    {
        var pg = ViewModel.PostgresConnectionSettings;
        PgHostBox.Text = pg.Host;
        PgPortBox.Text = pg.Port.ToString();
        PgDatabaseBox.Text = pg.Database;
        PgUsernameBox.Text = pg.Username;
        PgPasswordBox.Password = pg.Password;
        PgSchemaBox.Text = pg.Schema;

        var ora = ViewModel.OracleConnectionSettings;
        OraHostBox.Text = ora.Host;
        OraPortBox.Text = ora.Port.ToString();
        OraServiceNameBox.Text = ora.ServiceName;
        OraSidBox.Text = ora.Sid;
        OraByServiceNameRadio.IsChecked = !ora.ConnectBySid;
        OraBySidRadio.IsChecked = ora.ConnectBySid;
        OraUsernameBox.Text = ora.Username;
        OraPasswordBox.Password = ora.Password;
        OraSchemaBox.Text = ora.Schema;

        var my = ViewModel.MySqlConnectionSettings;
        MyHostBox.Text = my.Host;
        MyPortBox.Text = my.Port.ToString();
        MyDatabaseBox.Text = my.Database;
        MyUsernameBox.Text = my.Username;
        MyPasswordBox.Password = my.Password;

        var ms = ViewModel.SqlServerConnectionSettings;
        MsHostBox.Text = ms.Host;
        MsPortBox.Text = ms.Port.ToString();
        MsDatabaseBox.Text = ms.Database;
        MsWindowsAuthCheckBox.IsChecked = ms.UseWindowsAuthentication;
        MsUsernameBox.Text = ms.Username;
        MsPasswordBox.Password = ms.Password;
        MsSchemaBox.Text = ms.Schema;
        UpdateSqlServerLoginBoxes();

        SettingsPivot.SelectedIndex = (int)ViewModel.SelectedSource; // mở sẵn tab của nguồn đang chọn
        DatabaseSettingsDialog.Title = $"Thiết lập kết nối - hồ sơ \"{ViewModel.CurrentProfile.Name}\"";
        DatabaseSettingsDialog.XamlRoot = Content.XamlRoot;
        await DatabaseSettingsDialog.ShowAsync();
    }

    private static int ParsePort(string text, int fallback) =>
        int.TryParse(text.Trim(), out var port) && port is > 0 and <= 65535 ? port : fallback;

    private void DatabaseSettingsDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ViewModel.SaveConnectionSettings(ReadPostgresBoxes(), ReadOracleBoxes(), ReadMySqlBoxes(), ReadSqlServerBoxes());
    }

    private PostgresConnectionSettings ReadPostgresBoxes() => new()
    {
        Host = PgHostBox.Text.Trim(),
        Port = ParsePort(PgPortBox.Text, 5432),
        Database = PgDatabaseBox.Text.Trim(),
        Username = PgUsernameBox.Text.Trim(),
        Password = PgPasswordBox.Password,
        Schema = PgSchemaBox.Text.Trim(),
    };

    private OracleConnectionSettings ReadOracleBoxes() => new()
    {
        Host = OraHostBox.Text.Trim(),
        Port = ParsePort(OraPortBox.Text, 1521),
        ConnectBySid = OraBySidRadio.IsChecked == true,
        ServiceName = OraServiceNameBox.Text.Trim(),
        Sid = OraSidBox.Text.Trim(),
        Username = OraUsernameBox.Text.Trim(),
        Password = OraPasswordBox.Password,
        Schema = OraSchemaBox.Text.Trim(),
    };

    private MySqlConnectionSettings ReadMySqlBoxes() => new()
    {
        Host = MyHostBox.Text.Trim(),
        Port = ParsePort(MyPortBox.Text, 3306),
        Database = MyDatabaseBox.Text.Trim(),
        Username = MyUsernameBox.Text.Trim(),
        Password = MyPasswordBox.Password,
    };

    private SqlServerConnectionSettings ReadSqlServerBoxes() => new()
    {
        Host = MsHostBox.Text.Trim(),
        Port = ParsePort(MsPortBox.Text, 1433),
        Database = MsDatabaseBox.Text.Trim(),
        UseWindowsAuthentication = MsWindowsAuthCheckBox.IsChecked == true,
        Username = MsUsernameBox.Text.Trim(),
        Password = MsPasswordBox.Password,
        Schema = MsSchemaBox.Text.Trim(),
    };

    // ----- Thử kết nối (trong hộp thoại thiết lập) -----

    private CancellationTokenSource? _testConnectionCts;

    /// <summary>Dialog's secondary button: connects with the values typed in the current tab (unsaved) and shows
    /// the result in the InfoBar above the tabs. Cancel = keep the dialog open.</summary>
    private async void TestConnection_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true; // must be set before the first await
        object settings = (DatabaseSourceType)SettingsPivot.SelectedIndex switch
        {
            DatabaseSourceType.Oracle => ReadOracleBoxes(),
            DatabaseSourceType.MySql => ReadMySqlBoxes(),
            DatabaseSourceType.SqlServer => ReadSqlServerBoxes(),
            _ => ReadPostgresBoxes(),
        };

        var missing = settings switch
        {
            PostgresConnectionSettings { Host: "" } or OracleConnectionSettings { Host: "" }
                or MySqlConnectionSettings { Host: "" } or SqlServerConnectionSettings { Host: "" } => "Host",
            PostgresConnectionSettings { Database: "" } or MySqlConnectionSettings { Database: "" }
                or SqlServerConnectionSettings { Database: "" } => "Database",
            OracleConnectionSettings { ConnectBySid: true, Sid: "" } => "SID",
            OracleConnectionSettings { ConnectBySid: false, ServiceName: "" } => "Service Name",
            _ => null,
        };
        if (missing is not null)
        {
            ShowTestResult(InfoBarSeverity.Warning, $"Chưa nhập {missing}.");
            return;
        }

        // Already testing: ignore the click. (Not disabling the button - that moves keyboard focus to "Huỷ",
        // and an Enter pressed while waiting would then close the dialog without saving.)
        if (_testConnectionCts is not null)
        {
            return;
        }

        var cts = _testConnectionCts = new CancellationTokenSource();
        TestConnectionProgress.Visibility = Visibility.Visible;
        ShowTestResult(InfoBarSeverity.Informational,
            $"Đang kết nối (tối đa {ViewModel.Options.EffectiveConnectTimeoutSeconds} giây)...");

        var (success, tableCount, message) = await ViewModel.TestConnectionAsync(settings, cts.Token);

        if (cts.IsCancellationRequested)
        {
            return; // dialog closed or tab switched meanwhile - the result no longer belongs on screen
        }

        _testConnectionCts = null;
        TestConnectionProgress.Visibility = Visibility.Collapsed;
        ShowTestResult(!success ? InfoBarSeverity.Error : tableCount == 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success, message);
    }

    private void ShowTestResult(InfoBarSeverity severity, string message)
    {
        TestConnectionInfoBar.Severity = severity;
        TestConnectionInfoBar.Message = message;
        TestConnectionInfoBar.IsOpen = true;
    }

    /// <summary>A result belongs to the tab it was run on - switching tab or closing the dialog drops it.</summary>
    private void ResetTestConnection()
    {
        _testConnectionCts?.Cancel();
        _testConnectionCts = null;
        TestConnectionProgress.Visibility = Visibility.Collapsed;
        TestConnectionInfoBar.IsOpen = false;
    }

    private void SettingsPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TestConnectionInfoBar is not null) // fires during InitializeComponent, before the InfoBar exists
        {
            ResetTestConnection();
        }
    }

    private void DatabaseSettingsDialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args) => ResetTestConnection();

    private void MsWindowsAuth_Changed(object sender, RoutedEventArgs e) => UpdateSqlServerLoginBoxes();

    private void UpdateSqlServerLoginBoxes()
    {
        var sqlLogin = MsWindowsAuthCheckBox.IsChecked != true;
        MsUsernameBox.IsEnabled = sqlLogin;
        MsPasswordBox.IsEnabled = sqlLogin;
    }

    // ----- Cài đặt -----

    private async void OpenOptionsDialog_Click(object sender, RoutedEventArgs e)
    {
        var options = ViewModel.Options;
        ConnectTimeoutBox.Value = options.EffectiveConnectTimeoutSeconds;
        CommandTimeoutBox.Value = options.EffectiveCommandTimeoutSeconds;
        OpenHtmlAfterExportSwitch.IsOn = options.OpenHtmlAfterExport;
        OpenFolderAfterExportSwitch.IsOn = options.OpenFolderAfterExport;
        HistoryCountBox.Value = options.EffectiveHistoryCount;

        OptionsDialog.XamlRoot = Content.XamlRoot;
        await OptionsDialog.ShowAsync();
    }

    private void OptionsDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // NumberBox.Value is NaN when the box was cleared - fall back to the default instead of saving 0.
        static int ToSeconds(double value, int fallback) => double.IsNaN(value) ? fallback : (int)Math.Round(value);

        ViewModel.SaveOptions(new AppOptions
        {
            ConnectTimeoutSeconds = ToSeconds(ConnectTimeoutBox.Value, AppOptions.DefaultConnectTimeoutSeconds),
            CommandTimeoutSeconds = ToSeconds(CommandTimeoutBox.Value, AppOptions.DefaultCommandTimeoutSeconds),
            OpenHtmlAfterExport = OpenHtmlAfterExportSwitch.IsOn,
            OpenFolderAfterExport = OpenFolderAfterExportSwitch.IsOn,
            HistoryCount = ToSeconds(HistoryCountBox.Value, AppOptions.DefaultHistoryCount),
        });
    }

    /// <summary>"Mặc định": fill the boxes with defaults but keep the dialog open so the user still confirms with Lưu.</summary>
    private void OptionsDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var defaults = new AppOptions();
        ConnectTimeoutBox.Value = defaults.ConnectTimeoutSeconds;
        CommandTimeoutBox.Value = defaults.CommandTimeoutSeconds;
        OpenHtmlAfterExportSwitch.IsOn = defaults.OpenHtmlAfterExport;
        OpenFolderAfterExportSwitch.IsOn = defaults.OpenFolderAfterExport;
        HistoryCountBox.Value = defaults.HistoryCount;
    }

    private void OraConnectType_Checked(object sender, RoutedEventArgs e)
    {
        // Guard against firing before InitializeComponent() has wired up both named elements yet.
        if (OraServiceNameBox is null || OraSidBox is null)
        {
            return;
        }

        var useSid = ReferenceEquals(sender, OraBySidRadio);
        OraServiceNameBox.IsEnabled = !useSid;
        OraSidBox.IsEnabled = useSid;
    }

    // ----- So sánh schema -----

    /// <summary>Mở hộp thoại chọn A / B: A chọn sẵn file của hồ sơ / nguồn đang chọn (chưa đọc thì file mới nhất),
    /// B = file mới nhất còn lại. Chưa đủ 2 file thì báo cách tạo.</summary>
    private async void OpenCompareDialog_Click(object sender, RoutedEventArgs e)
    {
        var files = ViewModel.ListSchemaFiles();
        if (files.Count < 2)
        {
            ShowErrorDialog("So sánh schema",
                $"Cần ít nhất 2 lần đọc database để so sánh (hiện có {files.Count}). Mỗi lần bấm \"1. Đọc database\" của 1 hồ sơ / nguồn " +
                "tạo 1 file - vd thêm hồ sơ \"prod\", đọc database ở cả hồ sơ dev và prod, rồi so sánh.");
            return;
        }

        CompareAComboBox.ItemsSource = files;
        CompareBComboBox.ItemsSource = files;
        var current = ViewModel.CurrentSchemaFilePath;
        var a = files.Find(f => string.Equals(f.Path, current, StringComparison.OrdinalIgnoreCase)) ?? files[0];
        CompareAComboBox.SelectedItem = a;
        // B: ưu tiên file của hồ sơ / nguồn khác (dev ↔ prod); bản cũ trong Lịch sử đã có nút "So với bản trước".
        CompareBComboBox.SelectedItem = files.Find(f => !ReferenceEquals(f, a) && !f.IsHistory) ?? files.Find(f => !ReferenceEquals(f, a));
        CompareError.Visibility = Visibility.Collapsed;
        CompareDialog.XamlRoot = Content.XamlRoot;
        if (await CompareDialog.ShowAsync() == ContentDialogResult.Primary
            && CompareAComboBox.SelectedItem is RdbmsHtmlGeneratorViewModel.SchemaFile fileA
            && CompareBComboBox.SelectedItem is RdbmsHtmlGeneratorViewModel.SchemaFile fileB
            && await ViewModel.CompareSchemasAsync(fileA, fileB) is { } error)
        {
            ShowErrorDialog("Không so sánh được", error);
        }
    }

    private void CompareSwap_Click(object sender, RoutedEventArgs e)
    {
        (CompareAComboBox.SelectedItem, CompareBComboBox.SelectedItem) = (CompareBComboBox.SelectedItem, CompareAComboBox.SelectedItem);
    }

    /// <summary>Chưa chọn đủ / chọn trùng 1 file → báo dưới 2 ô, giữ hộp thoại mở.</summary>
    private void CompareDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        string? error = CompareAComboBox.SelectedItem is null || CompareBComboBox.SelectedItem is null
            ? "Hãy chọn cả A và B."
            : ReferenceEquals(CompareAComboBox.SelectedItem, CompareBComboBox.SelectedItem) ? "Hãy chọn 2 file khác nhau."
            : null;
        if (error is not null)
        {
            args.Cancel = true;
            CompareError.Text = error;
            CompareError.Visibility = Visibility.Visible;
        }
    }

    // ----- Nhật ký -----

    /// <summary>Nút Copy cạnh tiêu đề Nhật ký: chép mọi dòng log (mỗi dòng 1 hàng) để dán vào tin nhắn / ghi chú lỗi.</summary>
    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.LogLines.Count == 0)
        {
            ViewModel.StatusText = "Nhật ký đang trống - chưa có gì để copy.";
            return;
        }
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(string.Join(Environment.NewLine, ViewModel.LogLines));
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        ViewModel.StatusText = $"Đã copy {ViewModel.LogLines.Count} dòng nhật ký.";
    }

    // ----- Hướng dẫn (F1) -----

    private SharedUI.Help.HelpWindow? _helpWindow;

    /// <summary>Mở cửa sổ Hướng dẫn (1 cửa sổ duy nhất - đang mở thì đưa lên trước). Nội dung: Views/HelpContent.</summary>
    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_helpWindow is null)
        {
            _helpWindow = new SharedUI.Help.HelpWindow("Rdbms.HtmlGenerator", "Schema PostgreSQL / Oracle → trang HTML tra cứu bảng / cột / khoá ngoại.",
                Views.HelpContent.Sections, searchPlaceholder: "Tìm tính năng, vd: Oracle SID, schema, khoá ngoại...");
            _helpWindow.Closed += (_, _) => _helpWindow = null;
        }
        _helpWindow.Activate();
    }
}
