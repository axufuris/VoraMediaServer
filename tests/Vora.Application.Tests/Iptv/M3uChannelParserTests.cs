using Vora.Application.Iptv;

namespace Vora.Application.Tests.Iptv;

public class M3uChannelParserTests
{
    private static readonly Guid PlaylistId = Guid.NewGuid();

    [Fact]
    public void Vlc_options_give_the_channel_its_user_agent_and_referer()
    {
        const string m3u = """
            #EXTM3U
            #EXTINF:-1 tvg-id="news.us" group-title="News",News 24
            #EXTVLCOPT:http-user-agent=Mozilla/5.0 (SMART-TV; Linux)
            #EXTVLCOPT:http-referrer=https://portal.example.com/
            https://iptv.example.com/news.m3u8
            """;

        var channel = M3uChannelParser.Parse(m3u, PlaylistId).Single();

        channel.StreamUrl.Should().Be("https://iptv.example.com/news.m3u8");
        channel.UserAgent.Should().Be("Mozilla/5.0 (SMART-TV; Linux)");
        channel.Referer.Should().Be("https://portal.example.com/");
    }

    [Fact]
    public void A_kodi_header_suffix_is_read_and_taken_off_the_address()
    {
        const string m3u = """
            #EXTM3U
            #EXTINF:-1 tvg-id="sport.uk",Sport 1
            https://iptv.example.com/sport.m3u8|User-Agent=Mozilla%2F5.0%20Kodi&Referer=https%3A%2F%2Fportal.example.com%2F&Origin=https://portal.example.com
            """;

        var channel = M3uChannelParser.Parse(m3u, PlaylistId).Single();

        channel.StreamUrl.Should().Be("https://iptv.example.com/sport.m3u8");
        channel.UserAgent.Should().Be("Mozilla/5.0 Kodi");
        channel.Referer.Should().Be("https://portal.example.com/");
    }

    [Fact]
    public void The_suffix_wins_over_vlc_options_and_extinf_attributes_are_read_as_before()
    {
        const string m3u = """
            #EXTM3U
            #EXTINF:-1 tvg-id="movies.uk" tvg-logo="https://logos.example.com/m.png" group-title="Movies",Movies HD
            #EXTVLCOPT:http-user-agent=VLC/3.0
            #EXTVLCOPT:network-caching=1000
            https://iptv.example.com/movies.ts|user-agent=TiviMate/4.7
            """;

        var channel = M3uChannelParser.Parse(m3u, PlaylistId).Single();

        channel.UserAgent.Should().Be("TiviMate/4.7");
        channel.Referer.Should().BeNull();
        channel.ExternalChannelId.Should().Be("movies.uk");
        channel.LogoUrl.Should().Be("https://logos.example.com/m.png");
        channel.GroupTitle.Should().Be("Movies");
        channel.Resolution.Should().Be("HD");
    }

    [Fact]
    public void Exthttp_and_kodi_properties_are_read_too()
    {
        const string m3u = """
            #EXTM3U
            #EXTINF:-1 tvg-id="a",A
            #EXTHTTP:{"User-Agent":"Agent A","Referer":"https://a.example.com/","Cookie":"x=1"}
            https://iptv.example.com/a.m3u8
            #EXTINF:-1 tvg-id="b",B
            #KODIPROP:inputstream.adaptive.stream_headers=User-Agent=Agent%20B&Referer=https://b.example.com/
            https://iptv.example.com/b.mpd
            """;

        var channels = M3uChannelParser.Parse(m3u, PlaylistId);

        channels[0].UserAgent.Should().Be("Agent A");
        channels[0].Referer.Should().Be("https://a.example.com/");
        channels[1].UserAgent.Should().Be("Agent B");
        channels[1].Referer.Should().Be("https://b.example.com/");
    }

    [Fact]
    public void A_channel_without_options_has_no_headers_of_its_own()
    {
        const string m3u = """
            #EXTM3U
            #EXTINF:-1 tvg-id="plain",Plain
            https://iptv.example.com/plain.m3u8
            """;

        var channel = M3uChannelParser.Parse(m3u, PlaylistId).Single();

        channel.UserAgent.Should().BeNull();
        channel.Referer.Should().BeNull();
    }

    [Fact]
    public void A_channel_without_a_tvg_id_keeps_the_id_it_had_before_the_suffix_was_taken_off()
    {
        const string url = "https://iptv.example.com/raw.ts|User-Agent=Agent";
        var m3u = $"#EXTM3U\n#EXTINF:-1,Raw\n{url}\n";
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(url))).ToLowerInvariant();

        var channel = M3uChannelParser.Parse(m3u, PlaylistId).Single();

        channel.ExternalChannelId.Should().Be(expected);
        channel.StreamUrl.Should().Be("https://iptv.example.com/raw.ts");
    }

    [Fact]
    public void A_pipe_that_is_not_a_header_suffix_stays_in_the_address()
    {
        const string m3u = """
            #EXTM3U
            #EXTINF:-1 tvg-id="odd",Odd
            https://iptv.example.com/odd|name.ts
            """;

        M3uChannelParser.Parse(m3u, PlaylistId).Single().StreamUrl.Should().Be("https://iptv.example.com/odd|name.ts");
    }

    [Fact]
    public void A_header_value_with_a_line_break_is_cleaned()
    {
        const string m3u = "#EXTM3U\n#EXTINF:-1 tvg-id=\"x\",X\nhttps://iptv.example.com/x.ts|User-Agent=A%0D%0AX-Evil:%201\n";

        M3uChannelParser.Parse(m3u, PlaylistId).Single().UserAgent.Should().Be("AX-Evil: 1");
    }
}
