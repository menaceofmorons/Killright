using Killright.Core.Models;
using Killright.Integration.Esi;
using Killright.Shared;
using Killright.Shared.Constants;
using Killright.Storage.Database;
using Killright.Storage.Diagnostics;
using Killright.Storage.Identity;
using Killright.Storage.Scan;

namespace Killright.UI.Scan;

public sealed class PilotIdentityResolver
{
    private readonly IEsiClient _esiClient;
    private readonly IPilotIdentityCache _identityCache;
    private readonly IEsiEntityNameCache _nameCache;
    private readonly int _maxConcurrency;
    private readonly Func<DateTime> _utcNow;

    public PilotIdentityResolver(
        IEsiClient esiClient,
        IPilotIdentityCache identityCache,
        IEsiEntityNameCache nameCache,
        int maxConcurrency,
        Func<DateTime>? utcNow = null)
    {
        _esiClient = esiClient;
        _identityCache = identityCache;
        _nameCache = nameCache;
        _maxConcurrency = maxConcurrency;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public async Task<IReadOnlyList<Pilot>> ResolveAsync(
        IReadOnlyList<string> inputNames,
        ScanTimings? timings = null,
        CancellationToken cancellationToken = default,
        ScanDatabaseSession? session = null,
        ScanWriteBatch? writes = null)
    {
        var now = _utcNow();
        var states = new Dictionary<string, PilotState>(StringComparer.Ordinal);

        foreach (var inputName in inputNames)
        {
            var key = PilotIdentityCacheRecord.NormalizeInputName(inputName);

            if (!states.ContainsKey(key))
                states[key] = new PilotState(key, inputName.Trim());
        }

        IReadOnlyDictionary<string, PilotIdentityCacheRecord> cachedRecords;

        try
        {
            cachedRecords = await _identityCache.GetRecordsAsync(states.Keys.ToList(), session, cancellationToken);
        }
        catch
        {
            cachedRecords = new Dictionary<string, PilotIdentityCacheRecord>();
        }

        foreach (var state in states.Values)
        {
            if (cachedRecords.TryGetValue(state.Key, out var cachedRecord))
                ApplyCached(state, cachedRecord, now);
        }

        var namesResolved = 0;

        var namesToResolve = states.Values.Where(state => state.Outcome is null && state.CharacterId is null).ToList();

        if (namesToResolve.Count > 0)
        {
            var lookups = await _esiClient.ResolveNamesAsync(
                namesToResolve.Select(state => state.InputName).ToList(),
                cancellationToken);

            foreach (var state in namesToResolve)
            {
                if (!lookups.TryGetValue(state.InputName, out var lookup) || lookup.Outcome == EsiLookupOutcome.Failed)
                {
                    state.Outcome = VerifyStatus.Failed;
                    continue;
                }

                if (lookup.Outcome == EsiLookupOutcome.NoMatch)
                {
                    state.Outcome = VerifyStatus.NoMatch;
                    state.Changed = true;
                    continue;
                }

                state.CharacterId = lookup.CharacterId;
                state.CharacterName = lookup.CharacterName;
                state.Changed = true;
                namesResolved++;
            }
        }

        var active = states.Values.Where(state => state.Outcome is null && state.CharacterId is not null).ToList();

        var needsCharacterCall = active
            .Where(state => state.SecurityStatusAtUtc is null
                || now - state.SecurityStatusAtUtc.Value > CacheDurations.SecurityStatus
                || state.Birthday is null)
            .ToList();

        var affiliationTask = _esiClient.GetAffiliationsAsync(
            active.Select(state => state.CharacterId!.Value).ToList(),
            cancellationToken);

        var characterTask = BoundedConcurrentRunner.RunAsync(
            needsCharacterCall,
            _maxConcurrency,
            (state, _, token) => _esiClient.GetCharacterDetailsAsync(state.CharacterId!.Value, token),
            (_, _) => (EsiCharacterDetails?)null,
            cancellationToken);

        await Task.WhenAll(affiliationTask, characterTask);

        var affiliations = affiliationTask.Result;
        var characterDetails = characterTask.Result;

        for (var index = 0; index < needsCharacterCall.Count; index++)
        {
            var details = characterDetails[index];

            if (details is null)
                continue;

            var state = needsCharacterCall[index];
            state.CharacterName = details.Name;
            state.CorporationId = details.CorporationId;
            state.AllianceId = details.AllianceId;
            state.IdsRefreshed = true;
            state.SecurityStatus = details.SecurityStatus;
            state.SecurityStatusAtUtc = now;
            state.Birthday = details.Birthday;
            state.FactionId = details.FactionId;
            state.Changed = true;
        }

        foreach (var state in active)
        {
            if (state.IdsRefreshed || !affiliations.TryGetValue(state.CharacterId!.Value, out var affiliation))
                continue;

            state.CorporationId = affiliation.CorporationId;
            state.AllianceId = affiliation.AllianceId;
            state.IdsRefreshed = true;

            if (state.Cached?.CorporationId != affiliation.CorporationId || state.Cached?.AllianceId != affiliation.AllianceId)
                state.Changed = true;
        }

        var entityNames = await ResolveEntityNamesAsync(active, session, writes, cancellationToken);
        namesResolved += entityNames.NewlyResolved;

        var pilotsByKey = new Dictionary<string, Pilot>(StringComparer.Ordinal);

        foreach (var state in states.Values)
        {
            pilotsByKey[state.Key] = state.ToPilot(entityNames.Names);

            if (state.Changed)
                await PersistAsync(state, now, writes);
        }

        timings.Add(ScanTimings.ScanLevel, ScanTimings.CounterPrefix + "esi_names_resolved", namesResolved);

        return inputNames
            .Select(inputName => pilotsByKey[PilotIdentityCacheRecord.NormalizeInputName(inputName)] with { InputName = inputName.Trim() })
            .ToList();
    }

    private static void ApplyCached(PilotState state, PilotIdentityCacheRecord record, DateTime now)
    {
        state.Cached = record;

        if (record.VerifyStatus == VerifyStatus.NoMatch)
        {
            if (now - record.CachedAtUtc <= CacheDurations.PilotIdentity)
                state.Outcome = VerifyStatus.NoMatch;

            return;
        }

        if (record.CharacterId is null)
            return;

        state.CharacterId = record.CharacterId;
        state.CharacterName = record.CharacterName;
        state.SecurityStatus = record.SecurityStatus;
        state.SecurityStatusAtUtc = record.SecurityStatusAtUtc;
        state.Birthday = record.Birthday;
        state.FactionId = record.FactionId;
    }

    private async Task<(IReadOnlyDictionary<long, string> Names, int NewlyResolved)> ResolveEntityNamesAsync(
        IReadOnlyList<PilotState> active,
        ScanDatabaseSession? session,
        ScanWriteBatch? writes,
        CancellationToken cancellationToken)
    {
        var corporationIds = active
            .Where(state => state.IdsRefreshed && state.CorporationId is not null)
            .Select(state => state.CorporationId!.Value)
            .ToHashSet();

        var allianceIds = active
            .Where(state => state.IdsRefreshed && state.AllianceId is not null)
            .Select(state => state.AllianceId!.Value)
            .ToHashSet();

        var wantedIds = corporationIds.Concat(allianceIds).Distinct().ToList();
        var names = new Dictionary<long, string>();

        if (wantedIds.Count == 0)
            return (names, 0);

        try
        {
            foreach (var pair in await _nameCache.GetNamesAsync(wantedIds, session, cancellationToken))
                names[pair.Key] = pair.Value;
        }
        catch
        {
        }

        var missingIds = wantedIds.Where(id => !names.ContainsKey(id)).ToList();

        if (missingIds.Count == 0)
            return (names, 0);

        var fetched = await _esiClient.GetEntityNamesAsync(missingIds, cancellationToken);

        if (fetched.Count == 0)
            return (names, 0);

        foreach (var pair in fetched)
            names[pair.Key] = pair.Value;

        var fetchedNames = fetched
            .Select(pair => new EsiEntityName(
                pair.Key,
                corporationIds.Contains(pair.Key) ? EsiEntityTypes.Corporation : EsiEntityTypes.Alliance,
                pair.Value))
            .ToList();

        if (writes is not null)
        {
            writes.AddEntityNames(fetchedNames);
            return (names, fetched.Count);
        }

        try
        {
            await _nameCache.UpsertAsync(fetchedNames, cancellationToken);
        }
        catch
        {
        }

        return (names, fetched.Count);
    }

    private async Task PersistAsync(PilotState state, DateTime now, ScanWriteBatch? writes)
    {
        var record = state.Outcome == VerifyStatus.NoMatch
            ? new PilotIdentityCacheRecord
            {
                InputName = state.Key,
                VerifyStatus = VerifyStatus.NoMatch,
                CachedAtUtc = now
            }
            : new PilotIdentityCacheRecord
            {
                InputName = state.Key,
                CharacterId = state.CharacterId,
                CharacterName = state.CharacterName,
                VerifyStatus = VerifyStatus.Partial,
                SecurityStatus = state.SecurityStatus,
                CorporationId = state.IdsRefreshed ? state.CorporationId : state.Cached?.CorporationId,
                AllianceId = state.IdsRefreshed ? state.AllianceId : state.Cached?.AllianceId,
                Birthday = state.Birthday,
                FactionId = state.FactionId,
                SecurityStatusAtUtc = state.SecurityStatusAtUtc,
                CachedAtUtc = now
            };

        if (writes is not null)
        {
            writes.AddIdentity(record);
            return;
        }

        try
        {
            await _identityCache.UpsertRecordAsync(record);
        }
        catch
        {
        }
    }

    private sealed class PilotState
    {
        public PilotState(string key, string inputName)
        {
            Key = key;
            InputName = inputName;
        }

        public string Key { get; }

        public string InputName { get; }

        public PilotIdentityCacheRecord? Cached { get; set; }

        public VerifyStatus? Outcome { get; set; }

        public bool Changed { get; set; }

        public long? CharacterId { get; set; }

        public string? CharacterName { get; set; }

        public double? SecurityStatus { get; set; }

        public DateTime? SecurityStatusAtUtc { get; set; }

        public DateOnly? Birthday { get; set; }

        public long? FactionId { get; set; }

        public bool IdsRefreshed { get; set; }

        public long? CorporationId { get; set; }

        public long? AllianceId { get; set; }

        public Pilot ToPilot(IReadOnlyDictionary<long, string> entityNames)
        {
            if (Outcome is { } outcome)
                return new Pilot { InputName = InputName, VerifyStatus = outcome };

            Corporation? corporation = null;
            Alliance? alliance = null;

            if (CorporationId is { } corporationId && entityNames.TryGetValue(corporationId, out var corporationName))
                corporation = new Corporation { CorporationId = corporationId, Name = corporationName };

            if (AllianceId is { } allianceId && entityNames.TryGetValue(allianceId, out var allianceName))
                alliance = new Alliance { AllianceId = allianceId, Name = allianceName };

            return new Pilot
            {
                InputName = InputName,
                CharacterId = CharacterId,
                CharacterName = CharacterName,
                VerifyStatus = VerifyStatus.Partial,
                SecurityStatus = SecurityStatus,
                Corporation = corporation,
                Alliance = alliance,
                AllianceId = AllianceId,
                Birthday = Birthday,
                FactionId = FactionId
            };
        }
    }
}
