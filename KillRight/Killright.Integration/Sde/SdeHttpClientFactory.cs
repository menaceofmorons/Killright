using System.Net;

namespace Killright.Integration.Sde;

public static class SdeHttpClientFactory
{
    public static HttpClient Create()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(SdeClientOptions.RequestTimeoutSeconds)
        };
    }
}
