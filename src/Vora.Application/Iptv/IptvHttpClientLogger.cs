using Microsoft.Extensions.Http.Logging;
using Microsoft.Extensions.Logging;

namespace Vora.Application.Iptv;

public class IptvHttpClientLogger : IHttpClientLogger
{
    private readonly ILogger<IptvHttpClientLogger> _logger;

    public IptvHttpClientLogger(ILogger<IptvHttpClientLogger> logger)
    {
        _logger = logger;
    }

    public object? LogRequestStart(HttpRequestMessage request) => null;

    public void LogRequestStop(object? context, HttpRequestMessage request, HttpResponseMessage response, TimeSpan elapsed) =>
        _logger.LogDebug("IPTV {Method} {Url} answered {Status} in {Elapsed} ms.", request.Method, Address(request), (int)response.StatusCode, (long)elapsed.TotalMilliseconds);

    public void LogRequestFailed(object? context, HttpRequestMessage request, HttpResponseMessage? response, Exception exception, TimeSpan elapsed) =>
        _logger.LogDebug("IPTV {Method} {Url} failed after {Elapsed} ms: {Error}", request.Method, Address(request), (long)elapsed.TotalMilliseconds, IptvUrlRedactor.Redact(exception.Message));

    private static string Address(HttpRequestMessage request)
    {
        if (request.RequestUri is not { IsAbsoluteUri: true } uri) return string.Empty;
        return IptvUrlRedactor.Redact(uri.GetLeftPart(UriPartial.Path));
    }
}
