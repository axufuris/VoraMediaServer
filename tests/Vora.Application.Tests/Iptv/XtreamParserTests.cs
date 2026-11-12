using Vora.Application.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Iptv;

public class XtreamParserTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_active_account_reads_its_status_expiry_connections_and_formats()
    {
        var account = XtreamParser.ParseAccount(XtreamFixtures.ActiveAccount);

        account.Status.Should().Be("Active");
        account.ExpiresAt.Should().Be(new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        account.MaxConnections.Should().Be(2);
        account.ActiveConnections.Should().Be(0);
        account.IsTrial.Should().BeFalse();
        account.AllowedOutputFormats.Should().Equal("m3u8", "ts", "rtmp");
        XtreamParser.AccountProblem(account, Now).Should().BeNull();
    }

    [Fact]
    public void Numbers_may_come_as_numbers_and_an_account_without_expiry_never_expires()
    {
        var account = XtreamParser.ParseAccount(XtreamFixtures.TransportStreamOnlyAccount);

        account.MaxConnections.Should().Be(1);
        account.ExpiresAt.Should().BeNull();
        XtreamParser.AccountProblem(account, Now).Should().BeNull();
    }

    [Fact]
    public void A_rejected_login_is_a_readable_error()
    {
        var act = () => XtreamParser.ParseAccount(XtreamFixtures.RejectedAccount);

        act.Should().Throw<XtreamAuthenticationException>().WithMessage("The provider rejected the username or password.");
    }

    [Theory]
    [InlineData("<html><body>Not found</body></html>")]
    [InlineData("[]")]
    [InlineData("{\"server_info\":{}}")]
    public void An_answer_that_is_not_an_xtream_account_is_refused(string body)
    {
        var act = () => XtreamParser.ParseAccount(body);

        act.Should().Throw<IptvSourceException>();
    }

    [Fact]
    public void An_expired_status_is_a_problem()
    {
        var account = XtreamParser.ParseAccount(XtreamFixtures.ExpiredStatusAccount);

        XtreamParser.AccountProblem(account, Now).Should().Be("The provider account has expired.");
    }

    [Fact]
    public void An_active_account_past_its_expiry_date_is_a_problem()
    {
        var account = XtreamParser.ParseAccount(XtreamFixtures.LapsedAccount);

        XtreamParser.AccountProblem(account, Now).Should().Be("The provider account expired on 2020-09-13.");
    }

    [Theory]
    [InlineData("Banned", "The provider account is banned.")]
    [InlineData("Disabled", "The provider account is disabled.")]
    [InlineData("Paused", "The provider account is not active (status: Paused).")]
    public void Other_statuses_are_named(string status, string message)
    {
        var account = new XtreamAccountInfo(status, null, 1, 0, false, Array.Empty<string>());

        XtreamParser.AccountProblem(account, Now).Should().Be(message);
    }

    [Fact]
    public void Hls_is_preferred_when_the_account_allows_it_and_ts_otherwise()
    {
        XtreamParser.PreferredExtension(new[] { "ts", "M3U8" }).Should().Be("m3u8");
        XtreamParser.PreferredExtension(new[] { "ts", "rtmp" }).Should().Be("ts");
        XtreamParser.PreferredExtension(Array.Empty<string>()).Should().Be("ts");
    }

    [Fact]
    public void Categories_map_their_ids_to_trimmed_names()
    {
        var categories = XtreamParser.ParseCategories(XtreamFixtures.Categories);

        categories.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["1"] = "UK | Entertainment",
            ["2"] = "News",
            ["3"] = "Radio"
        });
    }

    [Fact]
    public void Live_streams_become_channels_with_credential_free_references()
    {
        var playlistId = Guid.NewGuid();
        var channels = XtreamParser.ParseLiveStreams(XtreamFixtures.LiveStreams, playlistId, IptvChannelKind.Tv, XtreamParser.ParseCategories(XtreamFixtures.Categories), "m3u8");

        channels.Should().HaveCount(4);
        channels.Should().OnlyContain(c => c.PlaylistId == playlistId && c.StreamUrl.StartsWith("xtream:"));
        channels.Select(c => c.StreamUrl).Should().Equal("xtream:1002.m3u8", "xtream:1001.m3u8", "xtream:1003.m3u8", "xtream:1004.m3u8");
        channels.Should().NotContain(c => c.StreamUrl.Contains(XtreamFixtures.Password) || c.StreamUrl.Contains(XtreamFixtures.Username));

        var hd = channels.Single(c => c.StreamUrl == "xtream:1001.m3u8");
        hd.Name.Should().Be("BBC One");
        hd.Resolution.Should().Be("HD");
        hd.CountryCode.Should().Be("UK");
        hd.GroupTitle.Should().Be("UK | Entertainment");
        hd.LogoUrl.Should().Be("http://logos.example.com/bbc1.png");
        hd.Kind.Should().Be(IptvChannelKind.Tv);
    }

    [Fact]
    public void A_guide_id_shared_by_several_streams_stays_unique_and_still_matches_the_guide()
    {
        var channels = XtreamParser.ParseLiveStreams(XtreamFixtures.LiveStreams, Guid.NewGuid(), IptvChannelKind.Tv, new Dictionary<string, string>(), "ts");

        channels.Single(c => c.StreamUrl == "xtream:1001.ts").ExternalChannelId.Should().Be("bbc1.uk");
        channels.Single(c => c.StreamUrl == "xtream:1002.ts").ExternalChannelId.Should().Be("BBC1.uk@1002");
        channels.Select(c => c.ExternalChannelId).Should().OnlyHaveUniqueItems();
        channels.Select(c => c.ExternalChannelId.ToLowerInvariant()).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void A_stream_without_a_guide_id_gets_a_stable_id_from_its_stream_id()
    {
        var first = XtreamParser.ParseLiveStreams(XtreamFixtures.LiveStreams, Guid.NewGuid(), IptvChannelKind.Tv, new Dictionary<string, string>(), "ts");
        var again = XtreamParser.ParseLiveStreams(XtreamFixtures.LiveStreams, Guid.NewGuid(), IptvChannelKind.Tv, new Dictionary<string, string>(), "m3u8");

        first.Single(c => c.Name == "Sky News").ExternalChannelId.Should().Be("xtream-1003");
        again.Single(c => c.Name == "Sky News").ExternalChannelId.Should().Be("xtream-1003");
        first.Single(c => c.Name == "Sky News").LogoUrl.Should().BeNull();
    }

    [Fact]
    public void Radio_streams_are_radio_channels_and_read_their_category_from_the_id_list()
    {
        var channels = XtreamParser.ParseLiveStreams(XtreamFixtures.LiveStreams, Guid.NewGuid(), IptvChannelKind.Tv, XtreamParser.ParseCategories(XtreamFixtures.Categories), "ts");

        var radio = channels.Single(c => c.Name == "Jazz FM");
        radio.Kind.Should().Be(IptvChannelKind.Radio);
        radio.GroupTitle.Should().Be("Radio");
        radio.ExternalChannelId.Should().Be("xtream-1004");
    }

    [Fact]
    public void A_stream_id_that_could_change_the_address_is_skipped()
    {
        var channels = XtreamParser.ParseLiveStreams(XtreamFixtures.LiveStreams, Guid.NewGuid(), IptvChannelKind.Tv, new Dictionary<string, string>(), "ts");

        channels.Should().NotContain(c => c.Name == "Broken" || c.Name == "No id");
    }

    [Fact]
    public void A_channel_list_that_is_not_a_list_is_a_readable_error()
    {
        var act = () => XtreamParser.ParseLiveStreams(XtreamFixtures.RejectedAccount, Guid.NewGuid(), IptvChannelKind.Tv, new Dictionary<string, string>(), "ts");

        act.Should().Throw<IptvSourceException>().WithMessage("The provider's channel list could not be read.");
    }
}
