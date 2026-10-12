using System.Text.Json;
using Vora.Application.Artwork;
using Vora.Application.Iptv.ViewModels;
using Vora.Application.Podcasts.ViewModels;
using Vora.Application.Serialization;
using Vora.Application.SmartLists.ViewModels;

namespace Vora.Application.Tests.Artwork;

public class RemoteImageRegistryTests
{
    private const string StationLogo = "http://www.181.fm/images/181fm.png";

    private readonly RemoteImageRegistry _registry = new();

    private JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.RememberRemoteImages(_registry);
        return options;
    }

    [Fact]
    public void An_http_image_the_server_was_told_about_is_known()
    {
        _registry.Remember(StationLogo);

        _registry.IsKnown(StationLogo).Should().BeTrue();
    }

    [Theory]
    [InlineData("http://www.181.fm/images/other.png")]
    [InlineData("http://192.168.1.1/admin.png")]
    [InlineData(null)]
    [InlineData("")]
    public void An_address_it_was_never_told_about_is_not(string? url)
    {
        _registry.Remember(StationLogo);

        _registry.IsKnown(url).Should().BeFalse();
    }

    [Theory]
    [InlineData("https://example.com/logo.png")]
    [InlineData("/api/artwork/custom/logo.png")]
    [InlineData("data:image/png;base64,AAAA")]
    public void Only_http_addresses_are_kept(string url)
    {
        _registry.Remember(url);

        _registry.IsKnown(url).Should().BeFalse();
    }

    [Fact]
    public void A_channel_logo_is_remembered_when_the_channel_is_sent_to_a_client()
    {
        var json = JsonSerializer.Serialize(new IptvChannelVM { Name = "181.FM", LogoUrl = StationLogo }, Options());

        json.Should().Contain("\"logoUrl\":\"http://www.181.fm/images/181fm.png\"");
        _registry.IsKnown(StationLogo).Should().BeTrue();
    }

    [Fact]
    public void Logos_inside_a_list_of_home_row_entries_are_remembered()
    {
        var entries = new List<SmartListEntryVM>
        {
            new() { Title = "181.FM", ImageUrl = StationLogo, Channel = new IptvChannelVM { Name = "181.FM", LogoUrl = "http://www.181.fm/images/channel.png" } },
            new() { Title = "Echoes", ImageUrl = "http://echoesofbluemars.org/images/logo.jpg" }
        };

        JsonSerializer.Serialize(entries, Options());

        _registry.IsKnown(StationLogo).Should().BeTrue();
        _registry.IsKnown("http://www.181.fm/images/channel.png").Should().BeTrue();
        _registry.IsKnown("http://echoesofbluemars.org/images/logo.jpg").Should().BeTrue();
    }

    [Fact]
    public void Podcast_and_recording_artwork_is_remembered()
    {
        JsonSerializer.Serialize(new PodcastSubscriptionVM { ArtworkUrl = "http://feeds.example.com/show.jpg" }, Options());
        JsonSerializer.Serialize(new PodcastFeedEpisodeVM { ArtworkUrl = "http://feeds.example.com/episode.jpg", ShowArtworkUrl = "http://feeds.example.com/cover.jpg" }, Options());
        JsonSerializer.Serialize(new IptvRecordingChannelVM { LogoUrl = "http://tv.example.com/logo.png" }, Options());

        _registry.IsKnown("http://feeds.example.com/show.jpg").Should().BeTrue();
        _registry.IsKnown("http://feeds.example.com/episode.jpg").Should().BeTrue();
        _registry.IsKnown("http://feeds.example.com/cover.jpg").Should().BeTrue();
        _registry.IsKnown("http://tv.example.com/logo.png").Should().BeTrue();
    }

    [Fact]
    public void A_field_that_is_not_marked_as_a_remote_image_is_not_remembered()
    {
        JsonSerializer.Serialize(new IptvChannelVM { Name = "Feed", StreamUrl = "http://tv.example.com/live.m3u8" }, Options());

        _registry.IsKnown("http://tv.example.com/live.m3u8").Should().BeFalse();
    }

    [Fact]
    public void The_registry_starts_over_when_it_is_full()
    {
        for (var i = 0; i < RemoteImageRegistry.MaxEntries; i++)
        {
            _registry.Remember("http://example.com/" + i);
        }

        _registry.Remember(StationLogo);

        _registry.IsKnown(StationLogo).Should().BeTrue();
        _registry.IsKnown("http://example.com/0").Should().BeFalse();
    }
}
