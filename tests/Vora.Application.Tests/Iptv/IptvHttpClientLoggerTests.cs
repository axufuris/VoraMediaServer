using System.Net;
using Microsoft.Extensions.Logging;
using Vora.Application.Iptv;

namespace Vora.Application.Tests.Iptv;

public class IptvHttpClientLoggerTests
{
    private sealed class ListLogger : ILogger<IptvHttpClientLogger>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    [Fact]
    public void An_xtream_address_is_logged_without_the_account()
    {
        var sink = new ListLogger();
        var logger = new IptvHttpClientLogger(sink);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://provider.example.com:8080/live/viewer/example-password/1001.m3u8?token=abc");
        using var response = new HttpResponseMessage(HttpStatusCode.OK);

        logger.LogRequestStop(logger.LogRequestStart(request), request, response, TimeSpan.FromMilliseconds(12));
        logger.LogRequestFailed(null, request, null, new HttpRequestException("Connection refused"), TimeSpan.FromMilliseconds(3));

        sink.Messages.Should().HaveCount(2);
        sink.Messages.Should().OnlyContain(m => !m.Contains("viewer") && !m.Contains("example-password") && !m.Contains("abc"));
        sink.Messages[0].Should().Contain("http://provider.example.com:8080/live/***/***/1001.m3u8").And.Contain("200");
    }
}
