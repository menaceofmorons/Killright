using System.Collections.Concurrent;
using Killright.Integration.zKill;
using Killright.Shared.Constants;
using Killright.Shared.Killmails;
using Killright.Shared.zKill;
using Killright.Storage.Killmails;
using Killright.Storage.zKill;

namespace Killright.UI.InfoSheet;

public enum PilotLastActivitySource
{
    Cache,
    Live
}

public sealed record PilotLastActivityResolution(PilotRecentKillmail? Killmail, PilotLastActivitySource Source);

public sealed class PilotLastActivityResolver
{
    private readonly IzKillClient _client;
    private readonly IPilotLastKillmailCache _cache;
    private readonly IRecentKillmailCache _recentKillmails;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly ConcurrentDictionary<long, Lazy<Task<PilotLastActivityResolution>>> _inFlight = new();

    public PilotLastActivityResolver(
        IzKillClient client,
        IPilotLastKillmailCache cache,
        IRecentKillmailCache recentKillmails,
        Func<DateTimeOffset>? utcNow = null)
    {
        _client = client;
        _cache = cache;
        _recentKillmails = recentKillmails;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<PilotLastActivityResolution> ResolveAsync(long characterId)
    {
        var lookup = _inFlight.GetOrAdd(
            characterId,
            id => new Lazy<Task<PilotLastActivityResolution>>(() => RunAsync(id)));

        return lookup.Value;
    }

    private async Task<PilotLastActivityResolution> RunAsync(long characterId)
    {
        try
        {
            var now = _utcNow();
            var cached = await ReadCacheAsync(characterId);

            PilotRecentKillmail? live;
            PilotLastActivitySource source;

            if (cached is not null && now - cached.CheckedAtUtc < CacheDurations.LastKillmailLookup)
            {
                live = cached.HasKillmail ? cached.Killmail : null;
                source = PilotLastActivitySource.Cache;
            }
            else
            {
                var result = await _client.GetLastKillmailAsync(characterId);

                if (result.Outcome == zKillLastKillmailOutcome.Failure)
                    throw new InvalidOperationException("The zKill last killmail lookup failed.");

                live = Map(result, characterId);
                source = PilotLastActivitySource.Live;

                await WriteCacheAsync(new PilotLastKillmailRecord(
                    characterId,
                    live is not null,
                    result.Killmail?.KillmailId,
                    live,
                    now));
            }

            var local = await ReadLocalAsync(characterId);
            var chosen = local is not null && (live is null || local.KillTimeUtc > live.KillTimeUtc) ? local : live;

            return new PilotLastActivityResolution(chosen, source);
        }
        finally
        {
            _inFlight.TryRemove(characterId, out _);
        }
    }

    private async Task<PilotLastKillmailRecord?> ReadCacheAsync(long characterId)
    {
        try
        {
            return await _cache.GetAsync(characterId);
        }
        catch
        {
            return null;
        }
    }

    private async Task WriteCacheAsync(PilotLastKillmailRecord record)
    {
        try
        {
            await _cache.UpsertAsync(record);
        }
        catch
        {
        }
    }

    private async Task<PilotRecentKillmail?> ReadLocalAsync(long characterId)
    {
        try
        {
            return await _recentKillmails.GetMostRecentKillmailAsync(characterId);
        }
        catch
        {
            return null;
        }
    }

    private static PilotRecentKillmail? Map(zKillLastKillmailResult result, long characterId)
    {
        if (result.Killmail is not { } killmail || result.ActivityType is not { } activityType)
            return null;

        if (activityType == zKillActivityType.Loss)
        {
            return new PilotRecentKillmail(
                killmail.KillTimeUtc,
                zKillActivityType.Loss,
                killmail.SystemId,
                killmail.VictimShipTypeId,
                null,
                null,
                null);
        }

        var own = killmail.Attackers.FirstOrDefault(attacker => attacker.CharacterId == characterId);

        return new PilotRecentKillmail(
            killmail.KillTimeUtc,
            zKillActivityType.Kill,
            killmail.SystemId,
            own?.ShipTypeId,
            killmail.VictimShipTypeId,
            KillmailQualification.CountUniqueAttackers(killmail.Attackers),
            own?.WeaponTypeId);
    }
}
