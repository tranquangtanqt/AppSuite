using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModuleE.Models;
using ModuleE.ViewModels;

namespace ModuleE;

/// <summary>
/// Standalone data-dictionary tool for Module E: connects to PostgreSQL or Oracle, saves the schema
/// to SQLite, and exports a browsable static HTML report. Nothing here depends on how this process
/// was started.
/// </summary>
public sealed partial class MainWindow : Window
{
    public ModuleEViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
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

        DatabaseSettingsDialog.XamlRoot = Content.XamlRoot;
        await DatabaseSettingsDialog.ShowAsync();
    }

    private void DatabaseSettingsDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!int.TryParse(PgPortBox.Text, out var pgPort))
        {
            pgPort = 5432;
        }

        ViewModel.SavePostgresConnectionSettings(new PostgresConnectionSettings
        {
            Host = PgHostBox.Text.Trim(),
            Port = pgPort,
            Database = PgDatabaseBox.Text.Trim(),
            Username = PgUsernameBox.Text.Trim(),
            Password = PgPasswordBox.Password,
            Schema = PgSchemaBox.Text.Trim(),
        });

        if (!int.TryParse(OraPortBox.Text, out var oraPort))
        {
            oraPort = 1521;
        }

        ViewModel.SaveOracleConnectionSettings(new OracleConnectionSettings
        {
            Host = OraHostBox.Text.Trim(),
            Port = oraPort,
            ConnectBySid = OraBySidRadio.IsChecked == true,
            ServiceName = OraServiceNameBox.Text.Trim(),
            Sid = OraSidBox.Text.Trim(),
            Username = OraUsernameBox.Text.Trim(),
            Password = OraPasswordBox.Password,
            Schema = OraSchemaBox.Text.Trim(),
        });
    }

    private void SourceRadio_Checked(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedSource = ReferenceEquals(sender, SourceOracleRadio)
            ? Models.DatabaseSourceType.Oracle
            : Models.DatabaseSourceType.Postgres;
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
}
