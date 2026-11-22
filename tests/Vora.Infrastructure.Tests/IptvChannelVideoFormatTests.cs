using Microsoft.EntityFrameworkCore;
using Vora.Application.Iptv;
using Vora.Domain.Entities.Iptv;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class IptvChannelVideoFormatTests
{
    private static readonly LiveVideoFormat High10 = new("h264", "High 10", 51, 1080, 25, false);
    private static readonly DateTime SeenAt = new(2026, 10, 21, 20, 0, 0, DateTimeKind.Utc);

    private readonly string _database = "iptv-video-format-" + Guid.NewGuid().ToString("N");

    private VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>().UseInMemoryDatabase(_database).Options);

    private static IptvChannel Channel(Guid playlistId, string streamUrl) => new()
    {
        ExternalChannelId = "action24",
        Name = "Action 24 Not 24/7",
        StreamUrl = streamUrl,
        PlaylistId = playlistId
    };

    private async Task<(Guid PlaylistId, Guid ChannelId)> SeedAsync()
    {
        await using var db = NewContext();
        var playlist = new IptvPlaylist { Name = "Provider", M3uUrl = "https://iptv.example.com/list.m3u" };
        var channel = Channel(playlist.Id, "https://iptv.example.com/action24.ts");
        db.AddRange(playlist, channel);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await new IptvRepository(db).UpdateChannelVideoFormatAsync(channel.Id, High10, SeenAt);
        return (playlist.Id, channel.Id);
    }

    private async Task<IptvChannel> ChannelAsync(Guid channelId)
    {
        await using var db = NewContext();
        return await db.IptvChannels.AsNoTracking().SingleAsync(c => c.Id == channelId, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_format_a_session_saw_is_saved_on_the_channel()
    {
        var (_, channelId) = await SeedAsync();

        var channel = await ChannelAsync(channelId);

        channel.Should().BeEquivalentTo(new
        {
            VideoCodec = "h264",
            VideoProfile = "High 10",
            VideoLevel = (int?)51,
            VideoHeight = (int?)1080,
            VideoFrameRate = (double?)25,
            VideoInterlaced = (bool?)false,
            VideoFormatSeenAt = (DateTime?)SeenAt,
        });
    }

    [Fact]
    public async Task A_playlist_refresh_keeps_the_format_while_the_channel_keeps_its_address()
    {
        var (playlistId, channelId) = await SeedAsync();

        await using (var db = NewContext())
        {
            await new IptvRepository(db).UpdateChannelsAsync(playlistId, [Channel(playlistId, "https://iptv.example.com/action24.ts")]);
        }

        (await ChannelAsync(channelId)).VideoProfile.Should().Be("High 10");
    }

    [Fact]
    public async Task A_playlist_refresh_forgets_the_format_when_the_channel_moves_to_another_address()
    {
        var (playlistId, channelId) = await SeedAsync();

        await using (var db = NewContext())
        {
            await new IptvRepository(db).UpdateChannelsAsync(playlistId, [Channel(playlistId, "https://iptv.example.com/action24-hd.ts")]);
        }

        (await ChannelAsync(channelId)).Should().BeEquivalentTo(new
        {
            StreamUrl = "https://iptv.example.com/action24-hd.ts",
            VideoCodec = (string?)null,
            VideoProfile = (string?)null,
            VideoLevel = (int?)null,
            VideoFormatSeenAt = (DateTime?)null,
        });
    }

    private static IptvChannel Hinted(Guid playlistId, string codec)
    {
        var channel = Channel(playlistId, "https://iptv.example.com/action24.ts");
        channel.VideoCodec = codec;
        return channel;
    }

    [Fact]
    public async Task A_codec_the_lineup_names_fills_in_a_channel_that_has_no_format_yet()
    {
        await using var db = NewContext();
        var playlist = new IptvPlaylist { Name = "Tuner", M3uUrl = "x" };
        var channel = Channel(playlist.Id, "https://iptv.example.com/action24.ts");
        db.AddRange(playlist, channel);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await new IptvRepository(db).UpdateChannelsAsync(playlist.Id, [Hinted(playlist.Id, "mpeg2video")]);

        (await ChannelAsync(channel.Id)).Should().BeEquivalentTo(new { VideoCodec = "mpeg2video", VideoProfile = (string?)null, VideoInterlaced = (bool?)null, VideoFormatSeenAt = (DateTime?)null });
    }

    [Fact]
    public async Task A_codec_the_lineup_names_does_not_replace_what_playback_measured_for_the_same_codec()
    {
        var (playlistId, channelId) = await SeedAsync();

        await using (var db = NewContext())
        {
            await new IptvRepository(db).UpdateChannelsAsync(playlistId, [Hinted(playlistId, "H264".ToLowerInvariant())]);
        }

        (await ChannelAsync(channelId)).Should().BeEquivalentTo(new { VideoCodec = "h264", VideoProfile = "High 10", VideoLevel = (int?)51, VideoFormatSeenAt = (DateTime?)SeenAt });
    }

    [Fact]
    public async Task A_codec_the_lineup_names_replaces_a_measured_format_of_another_codec()
    {
        var (playlistId, channelId) = await SeedAsync();

        await using (var db = NewContext())
        {
            await new IptvRepository(db).UpdateChannelsAsync(playlistId, [Hinted(playlistId, "mpeg2video")]);
        }

        (await ChannelAsync(channelId)).Should().BeEquivalentTo(new { VideoCodec = "mpeg2video", VideoProfile = (string?)null, VideoLevel = (int?)null, VideoHeight = (int?)null, VideoInterlaced = (bool?)null, VideoFormatSeenAt = (DateTime?)null });
    }

    [Fact]
    public async Task A_lineup_that_names_no_codec_leaves_the_measured_format_alone()
    {
        var (playlistId, channelId) = await SeedAsync();

        await using (var db = NewContext())
        {
            await new IptvRepository(db).UpdateChannelsAsync(playlistId, [Channel(playlistId, "https://iptv.example.com/action24.ts")]);
        }

        (await ChannelAsync(channelId)).VideoCodec.Should().Be("h264");
    }
}
