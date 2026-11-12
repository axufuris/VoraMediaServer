using Vora.Application.Iptv;

namespace Vora.Application.Tests.Iptv;

public class IptvUrlRedactorTests
{
    [Theory]
    [InlineData("http://viewer:example-password@iptv.example.com/list.m3u", "http://***@iptv.example.com/list.m3u")]
    [InlineData("http://provider.example.com:8080/get.php?username=viewer&password=example-password&type=m3u_plus", "http://provider.example.com:8080/get.php?username=***&password=***&type=m3u_plus")]
    [InlineData("http://provider.example.com:8080/live/viewer/example-password/1001.ts", "http://provider.example.com:8080/live/***/***/1001.ts")]
    [InlineData("http://provider.example.com/movie/viewer/example-password/77.mkv", "http://provider.example.com/movie/***/***/77.mkv")]
    [InlineData("https://cdn.example.com/hls/stream.m3u8?token=abc123&expires=1700000000", "https://cdn.example.com/hls/stream.m3u8?token=***&expires=1700000000")]
    [InlineData("https://cdn.example.com/a.m3u8?API_KEY=abc&x=1", "https://cdn.example.com/a.m3u8?API_KEY=***&x=1")]
    public void Credentials_in_an_address_are_masked(string url, string expected)
    {
        IptvUrlRedactor.Redact(url).Should().Be(expected);
    }

    [Fact]
    public void Every_address_in_a_block_of_text_is_masked()
    {
        const string stderr = """
            [http @ 0x55] HTTP error 403 Forbidden
            http://provider.example.com:8080/live/viewer/example-password/1001.ts: Server returned 403 Forbidden (access denied)
            Opening 'http://viewer:example-password@iptv.example.com/a.ts?auth=xyz' for reading
            """;

        var redacted = IptvUrlRedactor.Redact(stderr);

        redacted.Should().NotContain("example-password").And.NotContain("viewer").And.NotContain("xyz");
        redacted.Should().Contain("HTTP error 403 Forbidden").And.Contain("/live/***/***/1001.ts");
    }

    [Fact]
    public void A_known_secret_is_masked_wherever_it_appears()
    {
        var redacted = IptvUrlRedactor.Redact("http://provider.example.com/hlsr/t0k3n/viewer/pa ss!/1001/1.ts and pa%20ss%21", "pa ss!");

        redacted.Should().NotContain("pa ss!").And.NotContain("pa%20ss%21");
    }

    [Fact]
    public void Text_without_credentials_is_left_alone()
    {
        const string text = "https://iptv-org.github.io/iptv/index.m3u returned 404 for channel 'News 24/7'";

        IptvUrlRedactor.Redact(text).Should().Be(text);
        IptvUrlRedactor.Redact(null).Should().BeEmpty();
    }

    [Theory]
    [InlineData("http://viewer:example-password@iptv.example.com/list.m3u", true)]
    [InlineData("http://provider.example.com/get.php?username=viewer&password=example-password", true)]
    [InlineData("http://provider.example.com/playlist.m3u?token=abc", true)]
    [InlineData("http://provider.example.com:8080/live/viewer/example-password/1001.ts", true)]
    [InlineData("http://provider.example.com:8080/live/viewer/example-password/1001", true)]
    [InlineData("https://iptv-org.github.io/iptv/index.m3u", false)]
    [InlineData("https://stream.example.com/live/radio1/stream/", false)]
    [InlineData("https://stream.example.com/radio.mp3?sid=1", false)]
    [InlineData("http://provider.example.com/list.m3u?token=", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Addresses_that_carry_credentials_are_recognised(string? url, bool expected)
    {
        IptvUrlRedactor.ContainsCredentials(url).Should().Be(expected);
    }

    [Theory]
    [InlineData("http://provider.example.com:8080/get.php?username=a&password=b", "http://provider.example.com")]
    [InlineData("https://viewer:pw@iptv.example.com/list.m3u", "https://iptv.example.com")]
    [InlineData("not a url", null)]
    public void Scheme_and_host_keeps_nothing_else(string url, string? expected)
    {
        IptvUrlRedactor.SchemeAndHost(url).Should().Be(expected);
    }
}
