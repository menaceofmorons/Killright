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
using Killright.UI.Diagnostics;
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

    private static IDisposable? _unobservedFailureSubscription;
    private static bool _startupRefused;
    private static readonly TimeSpan ShutdownWaitTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ShutdownCheckpointTimeout = TimeSpan.FromSeconds(5);

#if RELEASE_BUILD
    private const bool IsReleaseBuild = true;
#else
    private const bool IsReleaseBuild = false;
#endif

    protected override void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        _unobservedFailureSubscription = BackgroundFailureRecorder.Attach(EngineFailureLog.Record);

        var settingsPath = ApplicationSettingsLoader.GetDefaultSettingsPath();
        var settingsResult = ApplicationSettingsLoader.Load(settingsPath, EngineFailureLog.Record);

        if (!settingsResult.Succeeded)
        {
            EngineFailureLog.Record($"Startup refused: {settingsResult.Failure}");

            Shutdown();
            return;
        }

        Settings = settingsResult.Settings!;

        var dllPath = Path.Combine(
            AppContext.BaseDirectory,
            "killright_engine.dll");

        var engineRuntime =
            new KillrightEngineRuntime(
                dllPath,
                settingsPath);

        if (engineRuntime.InitializeFailedOnSettings)
        {
            EngineFailureLog.Record($"Startup refused: the engine rejected the settings file. {engineRuntime.InitializeFailureReason}");

            Shutdown();
            return;
        }

        EngineRuntime = engineRuntime;

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

        var backupFolder = Settings.BackupFolder
            ?? Path.Combine(Path.GetDirectoryName(databasePath)!, KillmailBackupDefaults.DefaultBackupFolderName);

        BackupService =
            new DatabaseBackupService(
                database,
                backupFolder,
                Settings.BackupRotationCount);

        var startupResult = GuardedStartup.Run(
            database,
            BackupService,
            IsReleaseBuild,
            Settings.QualificationFleetThreshold,
            message => EngineFailureLog.Record(message));

        if (startupResult.Outcome == StartupOutcome.DatabaseRefused)
        {
            MessageBox.Show(
                "The KillRight database could not be opened. Close any other program or KillRight window using it and start KillRight again.",
                "KillRight - Database",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            _startupRefused = true;
            Shutdown();
            return;
        }

        if (startupResult.Schema is { } schemaCheckResult)
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

            _startupRefused = true;
            Shutdown();
            return;
        }

        PilotIdentityCache =
            new PilotIdentityCache(database);
        EsiEntityNameCache =
            new EsiEntityNameCache(database);
        zKillActivityCache =
            new zKillActivityCache(database);
        RecentKillmailCache =
            new RecentKillmailCache(database, Settings.RecentWindowDays, RecordPurgeStatsTiming);
        PilotLastKillmailCache =
            new PilotLastKillmailCache(database);
        KillmailStore =
            new KillmailStore(database, Settings.QualificationFleetThreshold);
        zKillStatisticsCache =
            new zKillStatisticsCache(database);
        SdeReferenceDataStore =
            new SdeReferenceDataStore(database);

        var sdeHttpClient = SdeHttpClientFactory.Create();

        var sdeClient =
            new SdeClient(
                sdeHttpClient,
                new SdeClientOptions
                {
                    ManifestUrl = Settings.Sde.ManifestUrl,
                    DatasetZipUrl = Settings.Sde.DatasetZipUrl
                },
                EngineFailureLog.Record);

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
            _ = BackgroundFailureRecorder.RunAsync(
                () => Task.Run(() => sdeIngestionService.RunCheckWithRetryAsync()),
                "SDE update check",
                EngineFailureLog.Record);
        }

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
                esiHttpClient,
                log: EngineFailureLog.Record);
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

        _ = BackgroundFailureRecorder.RunAsync(
            () => PurgeScheduler.RunStartupPassAsync(),
            "Startup purge",
            EngineFailureLog.Record);
    }

    private static void RecordPurgePassTiming(string tag, double milliseconds)
    {
        if (!Settings.Timing.Enabled)
            return;

        var timings = new ScanTimings();
        timings.Record(ScanTimings.ScanLevel, "purge_pass", milliseconds, null, tag);
        timings.Flush();
    }

    private static void RecordPurgeStatsTiming(PurgePassStats stats)
    {
        if (!Settings.Timing.Enabled)
            return;

        var timings = new ScanTimings();
        timings.Record(ScanTimings.ScanLevel, "purge_candidates", stats.Candidates, null, "purge");
        timings.Record(ScanTimings.ScanLevel, "purge_batches", stats.Batches, null, "purge");
        timings.Record(ScanTimings.ScanLevel, "purge_vacuum", stats.VacuumMilliseconds, null, "purge");
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
        var workStopped = true;

        try
        {
            RunShutdownStep("wait for running work", () =>
            {
                var deadline = Stopwatch.StartNew();

                ScanCoordinator?.CancelCurrent();

                var scanIdle = ScanCoordinator?.WaitForIdle(ShutdownWaitTimeout) ?? true;
                var purgeIdle = PurgeScheduler?.WaitForIdle(
                    deadline.Elapsed >= ShutdownWaitTimeout ? TimeSpan.Zero : ShutdownWaitTimeout - deadline.Elapsed) ?? true;

                workStopped = scanIdle && purgeIdle;

                if (!workStopped)
                    EngineFailureLog.Record("Shutdown continued while a scan or purge pass was still running.");
            });

            if (ShutdownPolicy.ShouldBackup(_startupRefused, SkipBackupOnClose))
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
                if (!ShutdownPolicy.ShouldCheckpoint(_startupRefused) || Database is null || !Database.IsOpen)
                    return;

                if (!workStopped)
                {
                    EngineFailureLog.Record("Shutdown checkpoint skipped because a scan or purge pass did not stop.");
                    return;
                }

                var startTimestamp = Stopwatch.GetTimestamp();

                if (!Database.Checkpoint(ShutdownCheckpointTimeout))
                {
                    EngineFailureLog.Record("Shutdown checkpoint skipped because the write gate was not released in time.");
                    return;
                }

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