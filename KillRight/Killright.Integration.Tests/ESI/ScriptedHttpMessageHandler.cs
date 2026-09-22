using System.Net;

namespace Killright.Integration.Tests.Esi;

internal sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(string UriContains, Func<HttpResponseMessage> Respond)> _routes = [];
    private readonly List<(string UriContains, Exception Exception)> _throwRoutes = [];

    public ScriptedHttpMessageHandler OnUriContaining(string uriContains, HttpStatusCode statusCode, string? jsonBody = null)
    {
        _routes.Add((uriContains, () => new HttpResponseMessage(statusCode)
        {
            Content = jsonBody is null ? null : new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json")
        }));
        return this;
    }

    public ScriptedHttpMessageHandler ThrowOnUriContaining(string uriContains, Exception exception)
    {
        _throwRoutes.Add((uriContains, exception));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!.ToString();

        var throwRoute = _throwRoutes.FirstOrDefault(r => uri.Contains(r.UriContains, StringComparison.Ordinal));
        if (throwRoute.UriContains is not null)
            throw throwRoute.Exception;

        var route = _routes.FirstOrDefault(r => uri.Contains(r.UriContains, StringComparison.Ordinal));
        if (route.UriContains is not null)
            return Task.FromResult(route.Respond());

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
