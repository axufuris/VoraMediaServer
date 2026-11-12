using Vora.Application.Iptv;

namespace Vora.Application.Tests.Iptv;

public class PassthroughPlaylistAddressTests
{
    private static readonly Uri Playlist = new("http://provider.example.com:8080/live/viewer/example-password/1001.m3u8");

    [Theory]
    [InlineData("/hls/abc/1001_1.ts", "http://provider.example.com:8080/hls/abc/1001_1.ts")]
    [InlineData("1001_1.ts", "http://provider.example.com:8080/live/viewer/example-password/1001_1.ts")]
    [InlineData("//cdn.example.com/a/1.ts", "http://cdn.example.com/a/1.ts")]
    [InlineData("https://cdn.example.com/a/1.ts", "https://cdn.example.com/a/1.ts")]
    public void A_segment_address_is_resolved_against_the_playlist(string reference, string expected)
    {
        IptvPassthroughService.ResolveUri(reference, Playlist).Should().Be(expected);
    }

    [Fact]
    public void A_root_relative_path_is_never_read_as_a_file()
    {
        IptvPassthroughService.ResolveUri("/etc/passwd", Playlist).Should().StartWith("http://provider.example.com:8080/");
    }
}
