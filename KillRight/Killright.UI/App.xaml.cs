using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using Killright.Integration.Esi;
using Killright.Integration.zKill;
using Killright.Storage.Database;
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
}