using System.Net;
using System.Text;
using Vora.Application.Iptv;

namespace Vora.Application.Tests.Iptv;

public sealed class StubUpstream : HttpMessageHandler, IHttpClientFactory
{
    private readonly List<(Func<Uri, bool> Matches, Func<HttpResponseMessage> Respond)> _routes = new();
    private readonly List<(Func<Uri, bool> Matches, Exception Error)> _failures = new();

    public List<HttpRequestMessage> Requests { get; } = new();

    public StubUpstream On(Func<Uri, bool> matches, string body, string contentType = "application/json", HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes.Add((matches, () => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, contentType) }));
        return this;
    }

    public StubUpstream Fail(Func<Uri, bool> matches, Exception error)
    {
        _failures.Add((matches, error));
        return this;
    }

    public StubUpstream OnAction(string? action, string body) =>
        On(uri => uri.AbsolutePath.EndsWith("/player_api.php", StringComparison.Ordinal)
            && (action is null ? !uri.Query.Contains("action=", StringComparison.Ordinal) : uri.Query.Contains("action=" + action, StringComparison.Ordinal)), body);

    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient(this, disposeHandler: false);
        if (name == IptvManager.HttpClientName)
        {
            client.DefaultRequestHeaders.Add("User-Agent", "DefaultAgent/1.0");
        }
        return client;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var uri = request.RequestUri ?? throw new InvalidOperationException("No address.");
        foreach (var (matches, error) in _failures)
        {
            if (matches(uri)) throw error;
        }
        foreach (var (matches, respond) in _routes)
        {
            if (matches(uri))
            {
                var response = respond();
                response.RequestMessage = request;
                return Task.FromResult(response);
            }
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request, Content = new StringContent(string.Empty) });
    }
}
