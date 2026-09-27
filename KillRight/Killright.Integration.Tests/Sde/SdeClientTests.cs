using System.Net;
using Killright.Integration.Sde;
using Killright.Integration.Tests.Esi;
using Killright.Shared.Sde;
using Xunit;

namespace Killright.Integration.Tests.Sde;

public sealed class SdeClientTests
{
    private const string ManifestUrl = "https://developers.eveonline.com/static-data/tranquility/latest.jsonl";
    private const string ZipUrl = "https://developers.eveonline.com/static-data/eve-online-static-data-latest-jsonl.zip";

    [Fact]
    public async Task GetManifestAsync_SdeLinePresent_ReturnsBuildNumber()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("latest.jsonl", HttpStatusCode.OK,
                "{\"_key\": \"universe\", \"buildNumber\": 1}\n" +
                "{\"_key\": \"sde\", \"buildNumber\": 3542233, \"releaseDate\": \"2026-09-24T11:12:47Z\"}\n");

        var client = new SdeClient(new HttpClient(handler), Options());

        var result = await client.GetManifestAsync();

        Assert.Equal(SdeManifestOutcome.Success, result.Outcome);
        Assert.Equal(3542233, result.BuildNumber);
    }

    [Fact]
    public async Task GetManifestAsync_NoSdeLine_ReturnsFailure()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("latest.jsonl", HttpStatusCode.OK, "{\"_key\": \"universe\", \"buildNumber\": 1}\n");

        var client = new SdeClient(new HttpClient(handler), Options());

        var result = await client.GetManifestAsync();

        Assert.Equal(SdeManifestOutcome.Failure, result.Outcome);
        Assert.Null(result.BuildNumber);
    }

    [Fact]
    public async Task GetManifestAsync_HttpFailure_ReturnsFailure()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("latest.jsonl", HttpStatusCode.InternalServerError);

        var client = new SdeClient(new HttpClient(handler), Options());

        var result = await client.GetManifestAsync();

        Assert.Equal(SdeManifestOutcome.Failure, result.Outcome);
    }

    [Fact]
    public async Task GetManifestAsync_Throws_ReturnsFailure()
    {
        var handler = new ScriptedHttpMessageHandler()
            .ThrowOnUriContaining("latest.jsonl", new HttpRequestException());

        var client = new SdeClient(new HttpClient(handler), Options());

        var result = await client.GetManifestAsync();

        Assert.Equal(SdeManifestOutcome.Failure, result.Outcome);
    }

    [Fact]
    public async Task DownloadDatasetZipAsync_Success_WritesResponseBodyToDestination()
    {
        var body = new byte[] { 1, 2, 3, 4, 5 };
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContainingBytes("eve-online-static-data-latest-jsonl.zip", HttpStatusCode.OK, body);

        var client = new SdeClient(new HttpClient(handler), Options());
        var destinationPath = Path.Combine(Path.GetTempPath(), $"sde-download-{Guid.NewGuid():N}.zip");

        try
        {
            var result = await client.DownloadDatasetZipAsync(destinationPath);

            Assert.Equal(SdeDatasetDownloadOutcome.Success, result.Outcome);
            Assert.Equal(body, await File.ReadAllBytesAsync(destinationPath));
        }
        finally
        {
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
        }
    }

    [Fact]
    public async Task DownloadDatasetZipAsync_HttpFailure_ReturnsFailure()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("eve-online-static-data-latest-jsonl.zip", HttpStatusCode.InternalServerError);

        var client = new SdeClient(new HttpClient(handler), Options());
        var destinationPath = Path.Combine(Path.GetTempPath(), $"sde-download-{Guid.NewGuid():N}.zip");

        var result = await client.DownloadDatasetZipAsync(destinationPath);

        Assert.Equal(SdeDatasetDownloadOutcome.Failure, result.Outcome);
        Assert.False(File.Exists(destinationPath));
    }

    private static SdeClientOptions Options()
    {
        return new SdeClientOptions
        {
            ManifestUrl = ManifestUrl,
            DatasetZipUrl = ZipUrl
        };
    }
}
