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
using Killright.UI.Configuration;

namespace Killright.UI;

public partial class App : Application
{
    public static ApplicationSettings Settings { get; private set; } = null!;
    public static IEsiClient EsiClient { get; private set; } = null!;
    public static IzKillClient zKillClient { get; private set; } = null!;
    public static IPilotIdentityCache PilotIdentityCache { get; private set; } = null!;
    public static IzKillActivityCache zKillActivityCache { get; private set; } = null!;
    public static IRecentKillmailCache RecentKillmailCache { get; private set; } = null!;
    public static IzKillStatisticsCache zKillStatisticsCache { get; private set; } = null!;
    public static RustRecentStyleClient RecentStyleClient { get; private set; } = null!;
    public static IKillrightEngineRuntime EngineRuntime { get; private set; } = null!;
    public static KillRightDatabase Database { get; private set; } = null!;

    // Step 19.00.59: exposed for the not-yet-designed Historic Analysis
    // consumer (Section 6.9.5) to resolve the active historic database path
    // from later. GroupHistorySwapWatcher.CheckAndApply below is the only
    // thing that ever repoints it during this application's lifetime.
    public static GroupHistoryActiveDatabasePathResolver GroupHistoryActiveDatabasePathResolver { get; private set; } = null!;

    protected override void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);

        Settings = ApplicationSettingsLoader.LoadOrDefault();

        // Step 19.00.59: the application half of promotion (Design
        // Specification v5.4 Section 6.9.4) -- react once, at launch, to a
        // live flag left by a completed killright_history_updater build.
        // GroupHistorySwapWatcher.CheckAndApply is a no-op (NoSentinel) on
        // every launch where no build has completed since the last check,
        // which is the common case. A single startup check, not a recurring
        // timer or FileSystemWatcher, is deliberate: builds are already
        // infrequent, manually-triggered developer-menu actions (Section
        // 6.9.1), so the next launch after one completes is sufficient.
        // Wrapped so nothing about this new step can ever prevent KillRight
        // from starting -- GroupHistoryActiveDatabasePathResolver already
        // defaults to the previous active path (or the legacy default, on a
        // machine with no successful build yet) whether or not the check
        // below runs to completion.
        GroupHistoryActiveDatabasePathResolver = new GroupHistoryActiveDatabasePathResolver();

        try
        {
            new GroupHistorySwapWatcher(GroupHistoryActiveDatabasePathResolver).CheckAndApply();
        }
        catch
        {
            // Intentionally swallowed -- see comment above.
        }

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

        var esiHttpClient =
            new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(EsiClientOptions.RequestTimeoutSeconds)
            };
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
                zKillHandler)
            {
                Timeout = TimeSpan.FromSeconds(zKillClientOptions.RequestTimeoutSeconds)
            };

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