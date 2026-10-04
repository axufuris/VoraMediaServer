using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Vora.Application.Recommendations;
using Vora.Application.Recommendations.Providers;
using Vora.Application.Settings;

namespace Vora.Application.Tests.Recommendations;

public class OpenAiConnectionTestTests
{
    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private static (OpenAiRecommendationProvider Provider, StubHandler Handler) Provider(HttpStatusCode status)
    {
        var handler = new StubHandler(status);
        var provider = new OpenAiRecommendationProvider(
            Substitute.For<IOpenAiRecommendationRepository>(),
            Substitute.For<ISystemSettingsRepository>(),
            new HttpClient(handler),
            new MemoryCache(new MemoryCacheOptions()));
        return (provider, handler);
    }

    [Fact]
    public async Task A_key_openai_accepts_passes_and_is_sent_as_a_bearer_token()
    {
        var (provider, handler) = Provider(HttpStatusCode.OK);

        var result = await provider.TestConnectionAsync(new Dictionary<string, string> { ["api_key"] = " sk-test " }, TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Message.Should().Be("OpenAI accepted the API key.");
        (handler.Request?.RequestUri?.ToString()).Should().Be("https://api.openai.com/v1/models");
        (handler.Request?.Headers.Authorization?.ToString()).Should().Be("Bearer sk-test");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "OpenAI rejected the API key.")]
    [InlineData(HttpStatusCode.TooManyRequests, "OpenAI accepted the key but refused the request: check that the account has billing set up and credit left.")]
    [InlineData(HttpStatusCode.BadGateway, "Unexpected response from OpenAI (HTTP 502).")]
    public async Task A_refused_key_says_why(HttpStatusCode status, string message)
    {
        var (provider, _) = Provider(status);

        var result = await provider.TestConnectionAsync(new Dictionary<string, string> { ["api_key"] = "sk-test" }, TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Message.Should().Be(message);
    }

    [Fact]
    public async Task No_key_is_not_sent_anywhere()
    {
        var (provider, handler) = Provider(HttpStatusCode.OK);

        var result = await provider.TestConnectionAsync(new Dictionary<string, string>(), TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        handler.Request.Should().BeNull();
    }
}
