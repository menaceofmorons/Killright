using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using Killright.Integration.Esi;
using Killright.Integration.RateLimiting;
using Killright.Integration.Sde;
using Killright.Integration.zKill;
using Killright.Shared.Killmails;
using Killright.Shared.Time;
using Killright.Storage.Database;
using Killright.Storage.Diagnostics;
using Killright.Storage.Engine;
using Killright.Storage.Identity;
using Killright.Storage.Killmails;
using Killright.Storage.Sde;
using Killright.Storage.zKill;
using Killright.Core.Style;
using Killright.UI.Analysis;
using Killright.UI.Configuration;
using Killright.UI.InfoSheet;
using Killright.UI.Scan;
using Killright.UI.Sde;
using Killright.UI.Theme;
using Killright.UI.UiState;

namespace Killright.UI;

public partial class App : Application
{
    public static ApplicationSettings Settings { get; private set; } = null!;
    public static UiStateStore UiState { get; private set; } = null!;
    public static IEsiClient EsiClient { get; private set; } = null!;
    public static zKillClient zKillClient { get; private set; } = null!;
    public static IPilotIdentityCache PilotIdentityCache { get; private set; } = null!;
    public static IEsiEntityNameCache EsiEntityNameCache { get; private set; } = null!;
    public static IRequestStartLimiter zKillRequestLimiter { get; private set; } = null!;
    public static PilotIdentityResolver IdentityResolver { get; private set; } = null!;
    public static IzKillActivityCache zKillActivityCache { get; private set; } = null!;
    public static IRecentKillmailCache RecentKillmailCache { get; private set; } = null!;
    public static IPilotLastKillmailCache PilotLastKillmailCache { get; private set; } = null!;
    public static PilotLastActivityResolver LastActivityResolver { get; private set; } = null!;
    public static IKillmailStore KillmailStore { get; private set; } = null!;
    public static IzKillStatisticsCache zKillStatisticsCache { get; private set; } = null!;
    public static ISdeReferenceDataStore SdeReferenceDataStore { get; private set; } = null!;
    public static RustRecentStyleClient RecentStyleClient { get; private set; } = null!;
    public static IKillrightEngineRuntime EngineRuntime { get; private set; } = null!;
    public static KillRightDatabase Database { get; private set; } = null!;
    public static KillmailPurgeScheduler PurgeScheduler { get; private set; } = null!;
    public static IdleCheckpointScheduler IdleCheckpointScheduler { get; private set; } = null!;
    public static IEngineInputReader EngineInputReader { get; private set; } = null!;
    public static ScanCoordinator? ScanCoordinator { get; set; }
    public static IDatabaseBackupService BackupService { get; private set; } = null!;
    public static bool SkipBackupOnClose { get; set; }

    private static readonly TimeSpan ShutdownWaitTimeout = TimeSpan.FromSeconds(5);

#if ALPHA_RELEASE
    private const bool IsAlphaRelease = true;
#else
    private const bool IsAlphaRelease = false;
#endif

    protected override void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        var settingsPath = ApplicationSettingsLoader.GetDefaultSettingsPath();
        Settings = ApplicationSettingsLoader.LoadOrDefault(settingsPath);

        GeneralStyleClassifier.Configure(
            Settings.Style.BlobMinimumAverageAttackers,
            Settings.Style.FleetMinimumAverageAttackers,
            Settings.Style.PodderMinimumSharePercent,
            Settings.Style.PodderMinimumKillCount);

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
        AppearanceManager.ApplyNewPilotColor(UiState.Current.NewPilotColorHex, Settings.HighlightOpacity);

        var databasePath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "KillRight",
            "KillRight.db");

        var database =
            new KillRightDatabase(
                new KillRightDatabaseOptions
                {
                    DatabasePath = databasePath,
                    BusyTimeoutSeconds = Settings.Database.BusyTimeoutSeconds,
                    PageCacheMegabytes = Settings.Database.PageCacheMegabytes
                });

        Database = database;

        database.QuarantineIfCorrupt(reason =>
            EngineFailureLog.Record($"Operational database was corrupt at startup and has been rebuilt. {reason}"));

        var backupFolder = Settings.BackupFolder
            ?? Path.Combine(Path.GetDirectoryName(databasePath)!, KillmailBackupDefaults.DefaultBackupFolderName);

        BackupService =
            new DatabaseBackupService(
                database,
                backupFolder,
                Settings.BackupRotationCount);

        try
        {
            if (!File.Exists(databasePath) && BackupService.TryRestore())
                EngineFailureLog.Record("Restored the database from the latest backup.");

            database.EnsureCreated();
            database.Open();
        }
        catch (Exception exception)
        {
            EngineFailureLog.Record($"Startup refused: the operational database could not be opened. {exception.Message}");

            MessageBox.Show(
                "The KillRight database could not be opened. Close any other program or KillRight window using it and start KillRight again.",
                "KillRight - Database",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown();
            return;
        }

        var schemaCheckResult = SchemaVersionGate.CheckOnStartup(database, IsAlphaRelease);

        if (schemaCheckResult.Outcome != SchemaVersionCheckOutcome.Ok)
        {
            var message = schemaCheckResult.Outcome == SchemaVersionCheckOutcome.RefusedDowngrade
                ? $"This database's schema version ({schemaCheckResult.StoredVersion}) is newer than this build supports ({schemaCheckResult.CurrentVersion}). Install a newer version of KillRight to continue."
                : $"This database's schema version ({schemaCheckResult.StoredVersion}) has no migration path to the version this build requires ({schemaCheckResult.CurrentVersion}).";

            EngineFailureLog.Record($"Startup refused: {message}");

            MessageBox.Show(
                message,
                "KillRight - Schema Version",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown();
            return;
        }

        KillmailQualificationRequalifier.RequalifyOnStartup(database, Settings.QualificationFleetThreshold);

        PilotIdentityCache =
            new PilotIdentityCache(database);
        EsiEntityNameCache =
            new EsiEntityNameCache(database);
        zKillActivityCache =
            new zKillActivityCache(database);
        RecentKillmailCache =
            new RecentKillmailCache(database, Settings.RecentWindowDays);
        PilotLastKillmailCache =
            new PilotLastKillmailCache(database);
        KillmailStore =
            new KillmailStore(database, Settings.QualificationFleetThreshold);
        zKillStatisticsCache =
            new zKillStatisticsCache(database);
        SdeReferenceDataStore =
            new SdeReferenceDataStore(database);

        var sdeHttpClient =
            new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(SdeClientOptions.RequestTimeoutSeconds)
            };

        var sdeClient =
            new SdeClient(
                sdeHttpClient,
                new SdeClientOptions
                {
                    ManifestUrl = Settings.Sde.ManifestUrl,
                    DatasetZipUrl = Settings.Sde.DatasetZipUrl
                });

        var sdeIngestionService =
            new SdeIngestionService(
                sdeClient,
                SdeReferenceDataStore,
                Settings.Sde.CheckIntervalHours);

        var hasSdeReferenceData = SdeReferenceDataStore.HasReferenceDataAsync().GetAwaiter().GetResult();

        if (!hasSdeReferenceData)
        {
            new SdeFirstRunLoadWindow(sdeIngestionService).ShowDialog();
        }
        else
        {
            _ = Task.Run(() => sdeIngestionService.RunCheckWithRetryAsync());
        }

        var dllPath = Path.Combine(
            AppContext.BaseDirectory,
            "killright_engine.dll");

        EngineRuntime =
            new KillrightEngineRuntime(
                dllPath,
                settingsPath);

        if (!EngineRuntime.IsAvailable)
            EngineFailureLog.Record("killright_engine failed to initialize (killright_initialize did not return success).");

        EngineInputReader =
            new EngineInputReader(
                database,
                SdeReferenceDataStore,
                EngineFailureLog.Record);

        RecentStyleClient =
            new RustRecentStyleClient(
                EngineRuntime,
                EngineInputReader,
                Settings.ThreatBands);

        var esiHttpClient =
            new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(EsiClientOptions.RequestTimeoutSeconds)
            };
        EsiClient =
            new EsiClient(
                esiHttpClient);
        IdentityResolver =
            new PilotIdentityResolver(
                EsiClient,
                PilotIdentityCache,
                EsiEntityNameCache,
                Settings.Network.EsiMaxConcurrency);

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
                Timeout = Timeout.InfiniteTimeSpan
            };

        var zkillSettings = Settings.Network.Zkill;

        zKillRequestLimiter =
            new RollingWindowRequestBudget(
                zkillSettings.RequestBudget,
                TimeSpan.FromSeconds(zkillSettings.BudgetWindowSeconds));

        zKillClient =
            new zKillClient(
                zKillHttpClient,
                new zKillClientOptions
                {
                    RequestTimeout = TimeSpan.FromSeconds(zkillSettings.RequestTimeoutSeconds),
                    RetryCount = zkillSettings.RetryCount,
                    RateLimitPause = TimeSpan.FromSeconds(zkillSettings.RateLimitPauseSeconds)
                },
                zKillRequestLimiter,
                EngineFailureLog.Record);

        LastActivityResolver =
            new PilotLastActivityResolver(
                zKillClient,
                PilotLastKillmailCache,
                RecentKillmailCache,
                () => ApplicationClock.UtcNow);

        IdleCheckpointScheduler =
            new IdleCheckpointScheduler(
                TimeSpan.FromSeconds(Settings.Database.IdleCheckpointSeconds),
                database.HasPendingWal,
                database.Checkpoint,
                logFailure: EngineFailureLog.Record,
                recordCheckpoint: RecordCheckpointTiming);

        PurgeScheduler =
            new KillmailPurgeScheduler(
                RecentKillmailCache,
                logFailure: EngineFailureLog.Record,
                recordPass: RecordPurgePassTiming,
                passStarted: IdleCheckpointScheduler.ActivityStarted,
                passFinished: () => _ = IdleCheckpointScheduler.ActivityFinished());

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();

        _ = PurgeScheduler.RunStartupPassAsync();
    }

    private static void RecordPurgePassTiming(string tag, double milliseconds)
    {
        if (!Settings.Timing.Enabled)
            return;

        var timings = new ScanTimings();
        timings.Record(ScanTimings.ScanLevel, "purge_pass", milliseconds, null, tag);
        timings.Flush();
    }

    private static void RecordCheckpointTiming(string tag, double milliseconds)
    {
        if (!Settings.Timing.Enabled)
            return;

        var timings = new ScanTimings();
        timings.Record(ScanTimings.ScanLevel, "checkpoint", milliseconds, null, tag);
        timings.Flush();
    }

    private static void RunShutdownStep(string step, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            EngineFailureLog.Record($"Shutdown step '{step}' failed. {ex.Message}");
        }
    }

    protected override void OnExit(
        ExitEventArgs e)
    {
        try
        {
            RunShutdownStep("wait for running work", () =>
            {
                var deadline = Stopwatch.StartNew();

                ScanCoordinator?.CancelCurrent();

                var scanIdle = ScanCoordinator?.WaitForIdle(ShutdownWaitTimeout) ?? true;
                var purgeIdle = PurgeScheduler?.WaitForIdle(
                    deadline.Elapsed >= ShutdownWaitTimeout ? TimeSpan.Zero : ShutdownWaitTimeout - deadline.Elapsed) ?? true;

                if (!scanIdle || !purgeIdle)
                    EngineFailureLog.Record("Shutdown continued while a scan or purge pass was still running.");
            });

            if (!SkipBackupOnClose)
            {
                try
                {
                    BackupService?.BackupAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    EngineFailureLog.Record($"Backup on close failed; previous backup copy stands. {ex.Message}");
                }
            }

            RunShutdownStep("checkpoint", () =>
            {
                if (Database is null || !Database.IsOpen)
                    return;

                var startTimestamp = Stopwatch.GetTimestamp();
                Database.Checkpoint();
                RecordCheckpointTiming("shutdown", Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            });

            RunShutdownStep("close database", () => Database?.Close());
            RunShutdownStep("dispose engine", () => EngineRuntime?.Dispose());
        }
        finally
        {
            base.OnExit(e);
        }
    }

    private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        EngineFailureLog.Record($"Unhandled UI-thread exception: {e.Exception}");
        MessageBox.Show(e.Exception.ToString(), "KillRight - Unexpected Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            EngineFailureLog.Record($"Unhandled fatal exception: {ex}");
    }
}