using System.IO.Compression;
using System.Net;
using System.Text;
using Killright.Integration.Esi;
using Killright.Integration.Sde;
using Killright.Integration.zKill;
using Killright.Shared.Sde;
using Xunit;

namespace Killright.Integration.Tests;

public sealed class ClientIdentityTests
{
    private const string ManifestLine = "{\"_key\": \"sde\", \"buildNumber\": 4242}\n";

    [Fact]
    public void UserAgent_HasExpectedFormat_WithAssemblyVersionAndProjectUrl()
    {
        var version = typeof(ClientIdentity).Assembly.GetName().Version!;
        var expected = $"KillRight/{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)} (+https://github.com/menaceofmorons/Killright)";

        Assert.Equal(expected, ClientIdentity.UserAgent);
        Assert.DoesNotContain("@", ClientIdentity.UserAgent);
        Assert.DoesNotContain("Vsengne", ClientIdentity.UserAgent);
    }

    [Fact]
    public void AllThreeClients_SendTheSameUserAgent()
    {
        var esiHttp = new HttpClient();
        var zKillHttp = new HttpClient();
        var sdeHttp = new HttpClient();

        _ = new EsiClient(esiHttp);
        _ = new zKillClient(zKillHttp);
        _ = new SdeClient(sdeHttp);

        Assert.Equal(ClientIdentity.UserAgent, esiHttp.DefaultRequestHeaders.UserAgent.ToString());
        Assert.Equal(ClientIdentity.UserAgent, zKillHttp.DefaultRequestHeaders.UserAgent.ToString());
        Assert.Equal(ClientIdentity.UserAgent, sdeHttp.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Fact]
    public async Task SdeClient_GzipReply_IsParsed()
    {
        using var server = LoopbackServer.Start(gzip: true);

        var client = new SdeClient(SdeHttpClientFactory.Create(), new SdeClientOptions { ManifestUrl = server.Url });

        var result = await client.GetManifestAsync();

        Assert.Equal(SdeManifestOutcome.Success, result.Outcome);
        Assert.Equal(4242, result.BuildNumber);
    }

    [Fact]
    public async Task SdeClient_PlainReply_IsParsed()
    {
        using var server = LoopbackServer.Start(gzip: false);

        var client = new SdeClient(SdeHttpClientFactory.Create(), new SdeClientOptions { ManifestUrl = server.Url });

        var result = await client.GetManifestAsync();

        Assert.Equal(SdeManifestOutcome.Success, result.Outcome);
        Assert.Equal(4242, result.BuildNumber);
    }

    [Fact]
    public async Task SdeClient_GzipReply_WithoutDecompression_Fails()
    {
        using var server = LoopbackServer.Start(gzip: true);

        var client = new SdeClient(new HttpClient(), new SdeClientOptions { ManifestUrl = server.Url });

        var result = await client.GetManifestAsync();

        Assert.Equal(SdeManifestOutcome.Failure, result.Outcome);
    }

    private sealed class LoopbackServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _loop;

        private LoopbackServer(bool gzip)
        {
            var port = FreePort();
            Url = $"http://127.0.0.1:{port}/latest.jsonl";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _loop = Task.Run(() => Serve(gzip));
        }

        public string Url { get; }

        public static LoopbackServer Start(bool gzip) => new(gzip);

        public void Dispose()
        {
            _listener.Close();
            try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        }

        private static int FreePort()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private async Task Serve(bool gzip)
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }

                var plain = Encoding.UTF8.GetBytes(ManifestLine);
                var body = gzip ? Compress(plain) : plain;

                if (gzip)
                    context.Response.AddHeader("Content-Encoding", "gzip");

                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body);
                context.Response.Close();
            }
        }

        private static byte[] Compress(byte[] data)
        {
            using var output = new MemoryStream();
            using (var gzipStream = new GZipStream(output, CompressionLevel.Fastest))
                gzipStream.Write(data);
            return output.ToArray();
        }
    }
}
