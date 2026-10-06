using System.Net;
using System.Text;
using Killright.Integration.Esi;
using Xunit;

namespace Killright.Integration.Tests.Esi;

public sealed class EsiClientFactionTests
{
    [Theory]
    [InlineData("character-empire-militia.json", 500004L)]
    [InlineData("character-pirate-militia.json", 500010L)]
    public async Task GetCharacterDetailsAsync_EnlistedFixture_ParsesFactionId(string fixture, long expectedFactionId)
    {
        var client = CreateClient(fixture);

        var details = await client.GetCharacterDetailsAsync(1);

        Assert.NotNull(details);
        Assert.Equal(expectedFactionId, details!.FactionId);
    }

    [Fact]
    public async Task GetCharacterDetailsAsync_NotEnlistedFixture_FactionIdIsNull()
    {
        var client = CreateClient("character-not-enlisted.json");

        var details = await client.GetCharacterDetailsAsync(1);

        Assert.NotNull(details);
        Assert.Null(details!.FactionId);
    }

    private static EsiClient CreateClient(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ESI", "Fixtures", fixture);
        var body = File.ReadAllText(path);

        return new EsiClient(new HttpClient(new FixtureHandler(body)));
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly string _body;

        public FixtureHandler(string body)
        {
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }
}
