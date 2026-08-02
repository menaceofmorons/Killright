using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using Killright.Integration.Esi;
using Killright.Integration.zKill;
using Killright.Storage.Database;
using Killright.Storage.GroupHistory;
using Killright.Storage.Identity;
using Killright.Storage.Killmails;
using Killright.Storage.zKill;
using Killright.UI.Analysis;

namespace Killright.UI;

public partial class App : Application
{
    public static IEsiClient EsiClient { get; private set; } = null!;
    public static IzKillClient zKillClient { get; private set; } = null!;
    public static IPilotIdentityCache PilotIdentityCache { get; private set; } = null!;
    public static IzKillActivityCache zKillActivityCache { get; private set; } = null!;
    public static IRecentKillmailCache RecentKillmailCache { get; private set; } = null!;
    public static IzKillStatisticsCache zKillStatisticsCache { get; private set; } = null!;
    public static RustRecentStyleClient RecentStyleClient { get; private set; } = null!;
    public static IKillrightEngineRuntime EngineRuntime { get; private set; } = null!;
    public static KillRightDatabase Database { get; private set; } = null!;

    protected override void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);

        var databasePath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "KillRight",
            "KillRight.duckdb");

        var database =
            new KillRightDatabase(
                new KillRightDatabaseOptions
                {
                    DatabasePath = databasePath
                });

        Database = database;
        database.EnsureCreated();

        PilotIdentityCache =
            new DuckDbPilotIdentityCache(database);

        zKillActivityCache =
            new DuckDbzKillActivityCache(database);

        RecentKillmailCache =
            new DuckDbRecentKillmailCache(database);

        zKillStatisticsCache =
            new DuckDbzKillStatisticsCache(database);

        var dllPath = Path.Combine(
            AppContext.BaseDirectory,
            "killright_engine.dll");

        EngineRuntime =
            new KillrightEngineRuntime(
                dllPath,
                databasePath);

        RecentStyleClient =
            new RustRecentStyleClient(
                EngineRuntime);

        var esiHttpClient = new HttpClient();

        EsiClient =
            new EsiClient(
                esiHttpClient);

        var zKillHandler =
            new HttpClientHandler
            {
                AutomaticDecompression =
                    DecompressionMethods.GZip
                    | DecompressionMethods.Deflate
            };

        var zKillHttpClient =
            new HttpClient(
                zKillHandler);

        zKillClient =
            new zKillClient(
                zKillHttpClient);

        CheckGroupHistoryStartupAsync()
            .GetAwaiter()
            .GetResult();

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(
        ExitEventArgs e)
    {
        try
        {
            EngineRuntime?.Dispose();
        }
        finally
        {
            base.OnExit(e);
        }
    }

    private static async Task CheckGroupHistoryStartupAsync()
    {
        var groupHistoryDatabase = new DuckDbGroupHistoryDatabase();
        var startupService = new GroupHistoryStartupService(groupHistoryDatabase);
        var requirement = await startupService.GetStartupRequirementAsync();

        if (!requirement.UpdateRequired)
            return;

        if (requirement.InitialCreationRequired)
        {
            var result = MessageBox.Show(
                "Historic Group Detection data has not been created.\r\n\r\n" +
                "This optional data allows KillRight to identify long-term pilot associations.\r\n\r\n" +
                "Initial creation imports up to 10 years of completed daily history and may take approximately 30-40 minutes.\r\n\r\n" +
                "You may skip this step and continue using KillRight without historic data.\r\n\r\n" +
                "Create the historic database schema now?",
                "Historic Group Detection",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.Yes)
                return;

            await groupHistoryDatabase.EnsureCreatedAsync();

            MessageBox.Show(
                "Historic Group Detection database schema has been created.\r\n\r\n" +
                "Use the Developer history import window to run the initial import in a later implementation step.",
                "Historic Group Detection",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        if (requirement.PromptRequired)
        {
            var result = MessageBox.Show(
                requirement.Message + "\r\n\r\n" +
                "The update is estimated to take more than one minute.\r\n\r\n" +
                "Update now?",
                "Historic Group Detection Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.Yes)
                return;
        }

        MessageBox.Show(
            "Historic Group Detection data requires an update.\r\n\r\n" +
            "Use the Developer history import window to run the update in a later implementation step.",
            "Historic Group Detection Update",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}