using System.Net;
using Killright.Integration.Esi;
using Killright.Shared;
using Xunit;

namespace Killright.Integration.Tests.Esi;

public sealed class EsiClientTests
{
    [Fact]
    public async Task ResolvePilotAsync_FullResolutionWithAlliance_ReturnsPartialWithFullData()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("universe/ids", HttpStatusCode.OK, """{"characters":[{"id":95465499,"name":"T'ral Vsengne"}]}""")
            .OnUriContaining("characters/95465499", HttpStatusCode.OK, """{"name":"T'ral Vsengne","corporation_id":98765,"alliance_id":99001,"security_status":-1.2}""")
            .OnUriContaining("corporations/98765", HttpStatusCode.OK, """{"name":"Test Corp","ticker":"TSTC"}""")
            .OnUriContaining("alliances/99001", HttpStatusCode.OK, """{"name":"Test Alliance","ticker":"TSTA"}""");

        var client = new EsiClient(new HttpClient(handler));

        var pilot = await client.ResolvePilotAsync("T'ral Vsengne");

        Assert.Equal(VerifyStatus.Partial, pilot.VerifyStatus);
        Assert.Equal(95465499, pilot.CharacterId);
        Assert.NotNull(pilot.Corporation);
        Assert.NotNull(pilot.Alliance);
    }

    [Fact]
    public async Task ResolvePilotAsync_InputCasingDiffersFromCanonical_StillResolves()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("universe/ids", HttpStatusCode.OK, """{"characters":[{"id":95465499,"name":"T'ral Vsengne"}]}""")
            .OnUriContaining("characters/95465499", HttpStatusCode.OK, """{"name":"T'ral Vsengne","corporation_id":98765,"security_status":-1.2}""")
            .OnUriContaining("corporations/98765", HttpStatusCode.OK, """{"name":"Test Corp","ticker":"TSTC"}""");

        var client = new EsiClient(new HttpClient(handler));

        var pilot = await client.ResolvePilotAsync("T'RAL VSENGNE");

        Assert.Equal(VerifyStatus.Partial, pilot.VerifyStatus);
        Assert.Equal(95465499, pilot.CharacterId);
        Assert.Equal("T'ral Vsengne", pilot.CharacterName);
    }

    [Fact]
    public async Task ResolvePilotAsync_FullResolutionNoAlliance_ReturnsPartialWithFullData()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("universe/ids", HttpStatusCode.OK, """{"characters":[{"id":91321792,"name":"Lukas Naarii"}]}""")
            .OnUriContaining("characters/91321792", HttpStatusCode.OK, """{"name":"Lukas Naarii","corporation_id":98765,"security_status":0.4}""")
            .OnUriContaining("corporations/98765", HttpStatusCode.OK, """{"name":"Test Corp","ticker":"TSTC"}""");

        var client = new EsiClient(new HttpClient(handler));

        var pilot = await client.ResolvePilotAsync("Lukas Naarii");

        Assert.Equal(VerifyStatus.Partial, pilot.VerifyStatus);
        Assert.NotNull(pilot.Corporation);
        Assert.Null(pilot.AllianceId);
        Assert.Null(pilot.Alliance);
    }

    [Fact]
    public async Task ResolvePilotAsync_CorporationCallFails_ReturnsPartial()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("universe/ids", HttpStatusCode.OK, """{"characters":[{"id":91321792,"name":"Lukas Naarii"}]}""")
            .OnUriContaining("characters/91321792", HttpStatusCode.OK, """{"name":"Lukas Naarii","corporation_id":98765,"security_status":0.4}""")
            .OnUriContaining("corporations/98765", HttpStatusCode.InternalServerError);

        var client = new EsiClient(new HttpClient(handler));

        var pilot = await client.ResolvePilotAsync("Lukas Naarii");

        Assert.Equal(VerifyStatus.Partial, pilot.VerifyStatus);
        Assert.Null(pilot.Corporation);
    }

    [Fact]
    public async Task ResolvePilotAsync_AllianceCallFails_ReturnsPartialWithAllianceIdSet()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("universe/ids", HttpStatusCode.OK, """{"characters":[{"id":95465499,"name":"T'ral Vsengne"}]}""")
            .OnUriContaining("characters/95465499", HttpStatusCode.OK, """{"name":"T'ral Vsengne","corporation_id":98765,"alliance_id":99001,"security_status":-1.2}""")
            .OnUriContaining("corporations/98765", HttpStatusCode.OK, """{"name":"Test Corp","ticker":"TSTC"}""")
            .OnUriContaining("alliances/99001", HttpStatusCode.InternalServerError);

        var client = new EsiClient(new HttpClient(handler));

        var pilot = await client.ResolvePilotAsync("T'ral Vsengne");

        Assert.Equal(VerifyStatus.Partial, pilot.VerifyStatus);
        Assert.Null(pilot.Alliance);
        Assert.Equal(99001, pilot.AllianceId);
    }

    [Fact]
    public async Task ResolvePilotAsync_CharacterDetailCallFails_ReturnsPartial()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("universe/ids", HttpStatusCode.OK, """{"characters":[{"id":91321792,"name":"Lukas Naarii"}]}""")
            .OnUriContaining("characters/91321792", HttpStatusCode.InternalServerError);

        var client = new EsiClient(new HttpClient(handler));

        var pilot = await client.ResolvePilotAsync("Lukas Naarii");

        Assert.Equal(VerifyStatus.Partial, pilot.VerifyStatus);
        Assert.Equal(91321792, pilot.CharacterId);
    }

    [Fact]
    public async Task ResolvePilotAsync_NoCharacterInResponse_ReturnsNoMatch()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("universe/ids", HttpStatusCode.OK, """{"characters":[]}""");

        var client = new EsiClient(new HttpClient(handler));

        var pilot = await client.ResolvePilotAsync("syMptom NZ");

        Assert.Equal(VerifyStatus.NoMatch, pilot.VerifyStatus);
        Assert.Null(pilot.CharacterId);
    }

    [Fact]
    public async Task ResolvePilotAsync_NameLookupHttpFailure_ReturnsFailed()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("universe/ids", HttpStatusCode.InternalServerError);

        var client = new EsiClient(new HttpClient(handler));

        var pilot = await client.ResolvePilotAsync("syMptom NZ");

        Assert.Equal(VerifyStatus.Failed, pilot.VerifyStatus);
        Assert.Null(pilot.CharacterId);
    }

    [Fact]
    public async Task ResolvePilotAsync_NameLookupThrows_ReturnsFailed()
    {
        var handler = new ScriptedHttpMessageHandler()
            .ThrowOnUriContaining("universe/ids", new TaskCanceledException());

        var client = new EsiClient(new HttpClient(handler));

        var pilot = await client.ResolvePilotAsync("syMptom NZ");

        Assert.Equal(VerifyStatus.Failed, pilot.VerifyStatus);
    }

    [Fact]
    public async Task ResolvePilotsAsync_NameLookupThrowsForBatch_DoesNotAbortRemainingResolutions()
    {
        var handler = new ScriptedHttpMessageHandler()
            .ThrowOnUriContaining("universe/ids", new HttpRequestException());

        var client = new EsiClient(new HttpClient(handler));

        var pilots = await client.ResolvePilotsAsync(["Lukas Naarii", "T'ral Vsengne"]);

        Assert.Equal(2, pilots.Count);
        Assert.All(pilots, p => Assert.Equal(VerifyStatus.Failed, p.VerifyStatus));
    }

    [Fact]
    public async Task ResolveEntityByExactNameAsync_Corporation_ReturnsIdAndName()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("universe/ids", HttpStatusCode.OK, """{"corporations":[{"id":98765,"name":"Test Corp"}]}""");

        var client = new EsiClient(new HttpClient(handler));

        var resolved = await client.ResolveEntityByExactNameAsync("Test Corp", IgnoreEntryType.Corporation);

        Assert.NotNull(resolved);
        Assert.Equal(98765, resolved!.Id);
        Assert.Equal("Test Corp", resolved.Name);
    }

    [Fact]
    public async Task ResolveEntityByExactNameAsync_Alliance_ReturnsIdAndName()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("universe/ids", HttpStatusCode.OK, """{"alliances":[{"id":99001,"name":"Test Alliance"}]}""");

        var client = new EsiClient(new HttpClient(handler));

        var resolved = await client.ResolveEntityByExactNameAsync("Test Alliance", IgnoreEntryType.Alliance);

        Assert.NotNull(resolved);
        Assert.Equal(99001, resolved!.Id);
        Assert.Equal("Test Alliance", resolved.Name);
    }

    [Fact]
    public async Task ResolveEntityByExactNameAsync_Pilot_ReturnsIdAndName()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("universe/ids", HttpStatusCode.OK, """{"characters":[{"id":95465499,"name":"T'ral Vsengne"}]}""");

        var client = new EsiClient(new HttpClient(handler));

        var resolved = await client.ResolveEntityByExactNameAsync("T'ral Vsengne", IgnoreEntryType.Pilot);

        Assert.NotNull(resolved);
        Assert.Equal(95465499, resolved!.Id);
    }

    [Fact]
    public async Task ResolveEntityByExactNameAsync_NoMatch_ReturnsNull()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("universe/ids", HttpStatusCode.OK, """{"corporations":[]}""");

        var client = new EsiClient(new HttpClient(handler));

        var resolved = await client.ResolveEntityByExactNameAsync("Nonexistent Corp", IgnoreEntryType.Corporation);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveEntityByExactNameAsync_EsiUnreachable_ReturnsNull()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("universe/ids", HttpStatusCode.InternalServerError);

        var client = new EsiClient(new HttpClient(handler));

        var resolved = await client.ResolveEntityByExactNameAsync("Test Alliance", IgnoreEntryType.Alliance);

        Assert.Null(resolved);
    }
}
