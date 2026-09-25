using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using Killright.Integration.Esi;
using Killright.Integration.zKill;
using Killright.Shared.Killmails;
using Killright.Storage.Database;
using Killright.Storage.Diagnostics;
#if HISTORIC_RELATIONSHIPS
using Killright.Storage.GroupHistory;
#endif
using Killright.Storage.Identity;
using Killright.Storage.Killmails;
using Killright.Storage.zKill;
using Killright.UI.Analysis;
using Killright.UI.Configuration;
using Killright.UI.Theme;
using Killright.UI.UiState;

namespace Killright.UI;

public partial class App : Application
{
    public static ApplicationSettings Settings { get; private set; } = null!;
    public static UiStateStore UiState { get; private set; } = null!;
    public static IEsiClient EsiClient { get; private set; } = null!;
    public static IzKillClient zKillClient { get; private set; } = null!;
    public static IPilotIdentityCache PilotIdentityCache { get; private set; } = null!;
    public static IzKillActivityCache zKillActivityCache { get; private set; } = null!;
    public static IRecentKillmailCache RecentKillmailCache { get; private set; } = null!;
    public static IKillmailStore KillmailStore { get; private set; } = null!;
    public static IzKillStatisticsCache zKillStatisticsCache { get; private set; } = null!;
    public static RustRecentStyleClient RecentStyleClient { get; private set; } = null!;
    public static IKillrightEngineRuntime EngineRuntime { get; private set; } = null!;
    public static KillRightDatabase Database { get; private set; } = null!;
    public static IKillmailBackupService KillmailBackupService { get; private set; } = null!;
    public static bool SkipBackupOnClose { get; set; }

#if HISTORIC_RELATIONSHIPS
    // Step 19.00.59: exposed for the not-yet-designed Historic Analysis
    // consumer (Section 6.9.5) to resolve the active historic database path
    // from later. GroupHistorySwapWatcher.CheckAndApply below is the only
    // thing that ever repoints it during this application's lifetime.
    public static GroupHistoryActiveDatabasePathResolver GroupHistoryActiveDatabasePathResolver { get; private set; } = null!;
#endif

    protected override void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);

        Settings = ApplicationSettingsLoader.LoadOrDefault();

        UiState = new UiStateStore();

        if (UiState.WasCorruptOnLoad)
        {
            MessageBox.Show(
                "Saved window and display settings could not be read and have been reset to defaults.",
                "KillRight",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        AppearanceManager.ApplyTheme(UiState.Current.Theme);
        AppearanceManager.ApplyFontTier(UiState.Current.GridFontTier);

#if HISTORIC_RELATIONSHIPS
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
#endif

        var databasePath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "KillRight",
            "KillRight.duckdb");

        var databaseFileExistedBeforeStartup = File.Exists(databasePath);

        var database =
            new KillRightDatabase(
                new KillRightDatabaseOptions
                {
                    DatabasePath = databasePath
                });

        Database = database;

        var wasRecovered = database.EnsureCreatedWithRecovery(reason =>
            EngineFailureLog.Record($"Operational database was corrupt at startup and has been rebuilt. {reason}"));

        var backupFolder = Settings.BackupFolder
            ?? Path.Combine(Path.GetDirectoryName(databasePath)!, KillmailBackupDefaults.DefaultBackupFolderName);

        KillmailBackupService =
            new DuckDbKillmailBackupService(
                database,
                backupFolder,
                Settings.BackupRotationCount);

        if (wasRecovered || !databaseFileExistedBeforeStartup)
        {
            var restored = KillmailBackupService.TryRestoreAsync().GetAwaiter().GetResult();

            if (restored)
                EngineFailureLog.Record("Restored killmail and attacker tables from the latest backup.");
        }

        PilotIdentityCache =
            new DuckDbPilotIdentityCache(database);
        zKillActivityCache =
            new DuckDbzKillActivityCache(database);
        RecentKillmailCache =
            new DuckDbRecentKillmailCache(database, Settings.RecentWindowDays);
        KillmailStore =
            new DuckDbKillmailStore(database);
        zKillStatisticsCache =
            new DuckDbzKillStatisticsCache(database);

        var dllPath = Path.Combine(
            AppContext.BaseDirectory,
            "killright_engine.dll");

        EngineRuntime =
            new KillrightEngineRuntime(
                dllPath,
                databasePath);

        if (!EngineRuntime.IsAvailable)
            EngineFailureLog.Record("killright_engine failed to initialize (pintel_initialize did not return success).");

        RecentStyleClient =
            new RustRecentStyleClient(
                EngineRuntime,
                Settings.ThreatBands);

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
            if (!SkipBackupOnClose)
            {
                try
                {
                    KillmailBackupService?.BackupAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    EngineFailureLog.Record($"Backup on close failed; previous backup copy stands. {ex.Message}");
                }
            }

            EngineRuntime?.Dispose();
        }
        finally
        {
            base.OnExit(e);
        }
    }
}