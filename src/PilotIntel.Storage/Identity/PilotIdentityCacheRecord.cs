using PilotIntel.Core.Models;
using PilotIntel.Shared;

namespace PilotIntel.Storage.Identity;

public sealed class PilotIdentityCacheRecord
{
    public required string InputName { get; init; }
    public long? CharacterId { get; init; }
    public string? CharacterName { get; init; }
    public required VerifyStatus VerifyStatus { get; init; }
    public double? SecurityStatus { get; init; }
    public long? CorporationId { get; init; }
    public string? CorporationName { get; init; }
    public string? CorporationTicker { get; init; }
    public long? AllianceId { get; init; }
    public string? AllianceName { get; init; }
    public string? AllianceTicker { get; init; }
    public required DateTime CachedAtUtc { get; init; }

    public Pilot ToPilot()
    {
        var corporation = CorporationId.HasValue && !string.IsNullOrWhiteSpace(CorporationName)
            ? new Corporation { CorporationId = CorporationId.Value, Name = CorporationName, Ticker = CorporationTicker }
            : null;

        var alliance = AllianceId.HasValue && !string.IsNullOrWhiteSpace(AllianceName)
            ? new Alliance { AllianceId = AllianceId.Value, Name = AllianceName, Ticker = AllianceTicker }
            : null;

        return new Pilot
        {
            InputName = InputName,
            CharacterId = CharacterId,
            CharacterName = CharacterName,
            VerifyStatus = VerifyStatus,
            SecurityStatus = SecurityStatus,
            Corporation = corporation,
            Alliance = alliance
        };
    }

    public static PilotIdentityCacheRecord FromPilot(Pilot pilot)
    {
        return new PilotIdentityCacheRecord
        {
            InputName = pilot.InputName,
            CharacterId = pilot.CharacterId,
            CharacterName = pilot.CharacterName,
            VerifyStatus = pilot.VerifyStatus,
            SecurityStatus = pilot.SecurityStatus,
            CorporationId = pilot.Corporation?.CorporationId,
            CorporationName = pilot.Corporation?.Name,
            CorporationTicker = pilot.Corporation?.Ticker,
            AllianceId = pilot.Alliance?.AllianceId,
            AllianceName = pilot.Alliance?.Name,
            AllianceTicker = pilot.Alliance?.Ticker,
            CachedAtUtc = DateTime.UtcNow
        };
    }
}