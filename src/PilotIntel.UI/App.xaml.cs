using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using PilotIntel.Integration.Esi;
using PilotIntel.Integration.zKill;
using PilotIntel.Storage.Database;
using PilotIntel.Storage.Identity;
using PilotIntel.Storage.Killmails;
using PilotIntel.Storage.zKill;
using PilotIntel.UI.Analysis;

namespace PilotIntel.UI;

public partial class App : Application
{
    public static IEsiClient EsiClient { get; private set; } = null!;
    public static IzKillClient zKillClient { get; private set; } = null!;
    public static IPilotIdentityCache PilotIdentityCache { get; private set; } = null!;
    public static IzKillActivityCache zKillActivityCache { get; private set; } = null!;
    public static IRecentKillmailCache RecentKillmailCache { get; private set; } = null!;
    public static IzKillStatisticsCache zKillStatisticsCache { get; private set; } = null!;
    public static RustRecentStyleClient RecentStyleClient { get; private set; } = null!;
    public static IPIntelEngineRuntime EngineRuntime { get; private set; } = null!;
    public static PilotIntelDatabase Database { get; private set; } = null!;

    protected override void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);

        var databasePath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "PilotIntel",
            "PilotIntel.duckdb");

        var database =
            new PilotIntelDatabase(
                new PilotIntelDatabaseOptions
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
            "pintelengine.dll");

        EngineRuntime =
            new PIntelEngineRuntime(
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