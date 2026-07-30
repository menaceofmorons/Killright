using System.Diagnostics;
using System.IO;
using System.Text.Json;
using PilotIntel.Core.Style;
using PilotIntel.Shared.Killmails;

namespace PilotIntel.UI.Analysis;

public sealed class RustRecentStyleClient
{
    private const string EngineProjectDirectoryName = "PIntelEngine";
    private const string EngineRootDirectoryName = "engine";

    public async Task<StyleClassification> AnalyzeAsync(
        long characterId,
        IReadOnlyList<KillmailRecord> killmails,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var executablePath = ResolveExecutablePath();

            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                return StyleClassification.Unknown;

            var request = new RustAnalysisRequest(
                characterId,
                killmails.Select(RustKillmailInput.FromKillmail).ToList());

            var json = JsonSerializer.Serialize(request);

            using var process = new Process();

            process.StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();

            await process.StandardInput.WriteAsync(json);
            await process.StandardInput.FlushAsync(cancellationToken);
            process.StandardInput.Close();

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                return StyleClassification.Unknown;

            var result = JsonSerializer.Deserialize<RustAnalysisResult>(output);

            return MapRecentStyle(result?.recent_style);
        }
        catch
        {
            return StyleClassification.Unknown;
        }
    }

    private static string? ResolveExecutablePath()
    {
        var executableName = OperatingSystem.IsWindows()
            ? "pintelengine.exe"
            : "pintelengine";

        var deployedPath = Path.Combine(
            AppContext.BaseDirectory,
            executableName);

        if (File.Exists(deployedPath))
            return deployedPath;

        return ResolveRepositoryExecutablePath(executableName, "debug")
               ?? ResolveRepositoryExecutablePath(executableName, "release");
    }

    private static string? ResolveRepositoryExecutablePath(
        string executableName,
        string profile)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                EngineRootDirectoryName,
                EngineProjectDirectoryName,
                "target",
                profile,
                executableName);

            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }

    private static StyleClassification MapRecentStyle(
        string? value)
    {
        var normalized = value?.Trim();

        return normalized switch
        {
            RecentStyleContract.Unknown =>
                StyleClassification.Unknown,

            RecentStyleContract.Victim =>
                StyleClassification.Victim,

            RecentStyleContract.Solo =>
                StyleClassification.Solo,

            RecentStyleContract.Gang =>
                StyleClassification.Gang,

            RecentStyleContract.Blob =>
                StyleClassification.Blob,

            RecentStyleContract.Fleet =>
                StyleClassification.Fleet,

            RecentStyleContract.Miner =>
                StyleClassification.Miner,

            RecentStyleContract.Explorer =>
                StyleClassification.Explorer,

            RecentStyleContract.Hauler =>
                StyleClassification.Hauler,

            RecentStyleContract.PI =>
                StyleClassification.PI,

            LegacyRecentStyleDisplay.Unknown =>
                StyleClassification.Unknown,

            LegacyRecentStyleDisplay.Victim =>
                StyleClassification.Victim,

            LegacyRecentStyleDisplay.Miner =>
                StyleClassification.Miner,

            LegacyRecentStyleDisplay.Explorer =>
                StyleClassification.Explorer,

            LegacyRecentStyleDisplay.Hauler =>
                StyleClassification.Hauler,

            _ =>
                StyleClassification.Unknown
        };
    }

    private static class RecentStyleContract
    {
        public const string Unknown = "Unknown";
        public const string Victim = "Victim";
        public const string Solo = "Solo";
        public const string Gang = "Gang";
        public const string Blob = "Blob";
        public const string Fleet = "Fleet";
        public const string Miner = "Miner";
        public const string Explorer = "Explorer";
        public const string Hauler = "Hauler";
        public const string PI = "PI";
    }

    private static class LegacyRecentStyleDisplay
    {
        public const string Unknown = "Unk";
        public const string Victim = "Vict";
        public const string Miner = "Mine";
        public const string Explorer = "Explo";
        public const string Hauler = "Haul";
    }

    private sealed record RustAnalysisRequest(
        long character_id,
        IReadOnlyList<RustKillmailInput> killmails);

    private sealed record RustKillmailInput(
        long killmail_id,
        bool is_loss,
        int attacker_count,
        bool is_solo,
        long? ship_type_id)
    {
        public static RustKillmailInput FromKillmail(
            KillmailRecord killmail)
        {
            return new RustKillmailInput(
                killmail.KillmailId,
                killmail.IsLoss,
                killmail.AttackerCount,
                killmail.IsSolo,
                killmail.ShipTypeId);
        }
    }

    private sealed class RustAnalysisResult
    {
        public long character_id { get; set; }
        public string? recent_style { get; set; }
        public int analyzed_killmails { get; set; }
        public int kills { get; set; }
        public int losses { get; set; }
        public int solo_losses { get; set; }
    }
}




























