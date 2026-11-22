using System.Text;
using Vora.Application.Iptv;
using Vora.Application.Iptv.Dtos;
using Vora.Domain.Entities.Iptv;

namespace Vora.Application.Tests.Iptv;

public class XmlTvTunerAliasTests
{
    private readonly Guid _tuner = Guid.NewGuid();
    private readonly Guid _provider = Guid.NewGuid();

    private IptvChannel Tuner(string number, string name) => new() { ExternalChannelId = number, Name = name, StreamUrl = "http://192.168.1.50:5004/auto/v" + number, PlaylistId = _tuner };

    private IptvChannel Provider(string id, string name) => new() { ExternalChannelId = id, Name = name, StreamUrl = "https://p.example.com/" + id, PlaylistId = _provider };

    private static string Feed(string channels, params (string Channel, string Title)[] programmes)
    {
        var start = DateTime.UtcNow.AddHours(1);
        var builder = new StringBuilder("<?xml version=\"1.0\"?><tv>").Append(channels);
        foreach (var (channel, title) in programmes)
        {
            builder.Append($"<programme start=\"{start:yyyyMMddHHmmss} +0000\" stop=\"{start.AddHours(1):yyyyMMddHHmmss} +0000\" channel=\"{channel}\"><title>{title}</title></programme>");
        }
        return builder.Append("</tv>").ToString();
    }

    private async Task<Dictionary<string, List<IptvProgramDto>>> ParseAsync(string xml, IReadOnlyList<IptvChannel> channels, bool tunersKnown = true)
    {
        var parsed = new Dictionary<string, List<IptvProgramDto>>(StringComparer.OrdinalIgnoreCase);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        await XmlTvParser.ParseAsync(stream, channels.ToList(), parsed, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(7), TestContext.Current.CancellationToken,
            tunersKnown ? new HashSet<Guid> { _tuner } : null);
        return parsed;
    }

    [Fact]
    public async Task A_feed_that_names_channels_by_number_and_call_sign_reaches_the_tuner_channel_by_its_guide_number()
    {
        var xml = Feed(
            "<channel id=\"I2.1.12345.zap2it.com\"><display-name>2.1 WCBS</display-name><display-name>2.1</display-name><display-name>WCBS-DT</display-name></channel>",
            ("I2.1.12345.zap2it.com", "Evening News"));

        var parsed = await ParseAsync(xml, [Tuner("2.1", "WCBS-HD"), Tuner("7.1", "WABC-HD")]);

        parsed.Keys.Should().Equal("2.1");
        parsed["2.1"].Single().Title.Should().Be("Evening News");
        parsed["2.1"].Single().ChannelId.Should().Be("2.1");
    }

    [Fact]
    public async Task A_feed_with_only_call_signs_matches_a_tuner_channel_whatever_its_hd_or_dt_ending()
    {
        var xml = Feed("<channel id=\"abc-nyc\"><display-name>WABC-DT</display-name></channel>", ("abc-nyc", "Local News"));

        var parsed = await ParseAsync(xml, [Tuner("7.1", "WABC-HD")]);

        parsed["7.1"].Single().Title.Should().Be("Local News");
    }

    [Fact]
    public async Task An_xmltv_channel_whose_id_is_a_known_channel_is_matched_by_id_and_not_by_name()
    {
        var xml = Feed(
            "<channel id=\"7.1\"><display-name>WABC-DT</display-name></channel><channel id=\"other\"><display-name>7.1 WABC</display-name></channel>",
            ("7.1", "By id"), ("other", "By name"));

        var parsed = await ParseAsync(xml, [Tuner("7.1", "WABC-HD")]);

        parsed["7.1"].Select(p => p.Title).Should().Equal("By id");
    }

    [Fact]
    public async Task Ordinary_playlist_channels_are_never_matched_by_display_name()
    {
        var xml = Feed("<channel id=\"abc-nyc\"><display-name>WABC-DT</display-name><display-name>7.1</display-name></channel>", ("abc-nyc", "Local News"));

        var parsed = await ParseAsync(xml, [Provider("7.1", "WABC-HD")]);

        parsed.Should().BeEmpty();
    }

    [Fact]
    public async Task Nothing_is_matched_by_name_when_there_are_no_tuners()
    {
        var xml = Feed("<channel id=\"abc-nyc\"><display-name>7.1 WABC</display-name></channel>", ("abc-nyc", "Local News"));

        var parsed = await ParseAsync(xml, [Tuner("7.1", "WABC-HD")], tunersKnown: false);

        parsed.Should().BeEmpty();
    }

    [Fact]
    public async Task A_plain_number_needs_the_whole_name_or_the_call_sign_to_agree()
    {
        var xml = Feed(
            "<channel id=\"star\"><display-name>5 Star</display-name></channel>"
            + "<channel id=\"bare\"><display-name>5</display-name></channel>"
            + "<channel id=\"named\"><display-name>5 WXYZ</display-name></channel>",
            ("star", "Wrong"), ("bare", "Bare number"), ("named", "Named too"));

        var parsed = await ParseAsync(xml, [Tuner("5", "WXYZ-HD")]);

        parsed.Keys.Should().Equal("5");
        parsed["5"].Select(p => p.Title).Should().Equal("Bare number");
    }

    [Fact]
    public async Task A_programme_straight_after_an_unmatched_one_is_not_lost()
    {
        var xml = Feed("", ("unknown-1", "Other"), ("7.1", "First known"), ("unknown-2", "Other"), ("unknown-3", "Other"), ("7.1", "Second known"));

        var parsed = await ParseAsync(xml, [Tuner("7.1", "WABC-HD")], tunersKnown: false);

        parsed["7.1"].Select(p => p.Title).Should().Equal("First known", "Second known");
    }

    [Fact]
    public async Task A_programme_straight_after_one_outside_the_window_is_not_lost()
    {
        var past = DateTime.UtcNow.AddDays(-5);
        var xml = "<?xml version=\"1.0\"?><tv>"
            + $"<programme start=\"{past:yyyyMMddHHmmss} +0000\" stop=\"{past.AddHours(1):yyyyMMddHHmmss} +0000\" channel=\"7.1\"><title>Too old</title></programme>"
            + Feed("", ("7.1", "Upcoming")).Replace("<?xml version=\"1.0\"?><tv>", string.Empty);

        var parsed = await ParseAsync(xml, [Tuner("7.1", "WABC-HD")], tunersKnown: false);

        parsed["7.1"].Select(p => p.Title).Should().Equal("Upcoming");
    }

    [Fact]
    public async Task A_plain_number_with_the_matching_call_sign_is_enough()
    {
        var xml = Feed("<channel id=\"named\"><display-name>5 WXYZ</display-name></channel>", ("named", "Named"));

        var parsed = await ParseAsync(xml, [Tuner("5", "WXYZ-HD")]);

        parsed["5"].Single().Title.Should().Be("Named");
    }

    [Fact]
    public async Task The_first_xmltv_channel_to_claim_a_tuner_channel_keeps_it_so_programmes_do_not_stack()
    {
        var xml = Feed(
            "<channel id=\"first\"><display-name>7.1 WABC</display-name></channel><channel id=\"second\"><display-name>7.1 KABC</display-name></channel>",
            ("first", "From first"), ("second", "From second"));

        var parsed = await ParseAsync(xml, [Tuner("7.1", "WABC-HD")]);

        parsed["7.1"].Select(p => p.Title).Should().Equal("From first");
    }

    [Theory]
    [InlineData("7.1 WABC", "7.1")]
    [InlineData("7.1", "7.1")]
    [InlineData("11.12-KTLA", "11.12")]
    [InlineData("5 Star", "5")]
    [InlineData("WABC", null)]
    [InlineData("4K Movies", null)]
    public void The_leading_channel_number_of_a_display_name_is_read(string displayName, string? expected)
    {
        TunerGuideAliases.LeadingNumber(displayName).Should().Be(expected);
    }

    [Theory]
    [InlineData("WABC-HD", "wabc")]
    [InlineData("WABC-DT", "wabc")]
    [InlineData("KABC-TV", "kabc")]
    [InlineData("ESPN", "espn")]
    [InlineData("HD", null)]
    [InlineData("TV", null)]
    public void A_call_sign_ignores_a_trailing_hd_dt_or_tv(string name, string? expected)
    {
        TunerGuideAliases.CallSign(name).Should().Be(expected);
    }
}
