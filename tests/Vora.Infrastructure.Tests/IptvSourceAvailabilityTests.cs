using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

// This answers "would the Live TV or Radio page have anything on it", which is
// what every client's navigation now gates on. Saying yes when the page is empty
// is the bug it exists to stop; saying no when a source is there strips a
// working feature out of the nav.
public class IptvSourceAvailabilityTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("iptv-availability-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static IptvPlaylist Playlist(IptvChannelKind kind, bool isActive = true, string? m3uUrl = "https://example.test/list.m3u") =>
        new() { Id = Guid.NewGuid(), Name = $"{kind} list", DefaultChannelKind = kind, IsActive = isActive, M3uUrl = m3uUrl };

    private static IptvChannel Channel(IptvPlaylist playlist, IptvChannelKind kind, bool hidden = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            ExternalChannelId = Guid.NewGuid().ToString("N"),
            Name = $"{kind} channel",
            StreamUrl = "https://example.test/stream",
            Kind = kind,
            IsHiddenByAdmin = hidden,
            PlaylistId = playlist.Id
        };

    private static async Task<(bool Tv, bool Radio)> AvailabilityAsync(params object[] rows)
    {
        await using var db = NewContext();
        db.AddRange(rows);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return await new IptvRepository(db).GetSourceAvailabilityAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_server_with_no_playlists_has_neither()
    {
        var availability = await AvailabilityAsync();

        availability.Tv.Should().BeFalse();
        availability.Radio.Should().BeFalse();
    }

    [Fact]
    public async Task Tv_channels_make_live_tv_available_and_leave_radio_alone()
    {
        var playlist = Playlist(IptvChannelKind.Tv);

        var availability = await AvailabilityAsync(playlist, Channel(playlist, IptvChannelKind.Tv));

        availability.Tv.Should().BeTrue();
        availability.Radio.Should().BeFalse();
    }

    [Fact]
    public async Task Radio_channels_make_radio_available_and_leave_live_tv_alone()
    {
        var playlist = Playlist(IptvChannelKind.Radio);

        var availability = await AvailabilityAsync(playlist, Channel(playlist, IptvChannelKind.Radio));

        availability.Radio.Should().BeTrue();
        availability.Tv.Should().BeFalse();
    }

    // Per-channel Kind is what both clients filter on, so a radio station inside
    // a playlist marked TV still makes the Radio tab worth showing.
    [Fact]
    public async Task A_mixed_playlist_counts_for_both_by_its_channels()
    {
        var playlist = Playlist(IptvChannelKind.Tv);

        var availability = await AvailabilityAsync(
            playlist,
            Channel(playlist, IptvChannelKind.Tv),
            Channel(playlist, IptvChannelKind.Radio));

        availability.Tv.Should().BeTrue();
        availability.Radio.Should().BeTrue();
    }

    // A playlist added but not yet synced has no channel rows at all. Waiting
    // for the first scan would leave the feature dark for minutes after an admin
    // configured it.
    [Fact]
    public async Task An_unscanned_playlist_counts_on_its_url_and_default_kind()
    {
        var availability = await AvailabilityAsync(Playlist(IptvChannelKind.Tv));

        availability.Tv.Should().BeTrue();
        availability.Radio.Should().BeFalse();
    }

    [Fact]
    public async Task A_playlist_with_no_url_and_no_channels_counts_for_nothing()
    {
        var availability = await AvailabilityAsync(Playlist(IptvChannelKind.Tv, m3uUrl: null));

        availability.Tv.Should().BeFalse();
    }

    [Fact]
    public async Task A_deactivated_playlist_is_not_a_source()
    {
        var playlist = Playlist(IptvChannelKind.Tv, isActive: false);

        var availability = await AvailabilityAsync(playlist, Channel(playlist, IptvChannelKind.Tv));

        availability.Tv.Should().BeFalse();
    }

    // Every channel hidden is an empty page, which is the same outcome as no
    // playlist at all as far as a viewer is concerned.
    [Fact]
    public async Task Channels_the_admin_hid_do_not_make_a_feature_available()
    {
        var playlist = Playlist(IptvChannelKind.Radio, m3uUrl: null);

        var availability = await AvailabilityAsync(
            playlist,
            Channel(playlist, IptvChannelKind.Radio, hidden: true),
            Channel(playlist, IptvChannelKind.Radio, hidden: true));

        availability.Radio.Should().BeFalse();
    }

    [Fact]
    public async Task One_visible_channel_among_hidden_ones_is_enough()
    {
        var playlist = Playlist(IptvChannelKind.Radio, m3uUrl: null);

        var availability = await AvailabilityAsync(
            playlist,
            Channel(playlist, IptvChannelKind.Radio, hidden: true),
            Channel(playlist, IptvChannelKind.Radio));

        availability.Radio.Should().BeTrue();
    }
}
