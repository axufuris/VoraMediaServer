using System.Text.Json.Nodes;
using Vora.Application.Analysis;
using Vora.Application.Iptv;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Iptv;

public class ChannelFavoritesManagerTests
{
    private readonly IChannelFavoriteRepository _repository = Substitute.For<IChannelFavoriteRepository>();
    private readonly IClientNotifier _notifier = Substitute.For<IClientNotifier>();
    private readonly ChannelFavoritesManager _manager;
    private readonly Guid _profileId = Guid.NewGuid();
    private readonly IptvPlaylist _playlist = new() { Id = Guid.NewGuid(), Name = "Cable" };

    public ChannelFavoritesManagerTests()
    {
        _manager = new ChannelFavoritesManager(_repository, _notifier);
        _repository.GetFavoriteChannelsAsync(Arg.Any<Guid>(), Arg.Any<IptvChannelKind>()).Returns(new List<FavoriteChannel>());
        _repository.ReplaceFavoritesAsync(Arg.Any<Guid>(), Arg.Any<IptvChannelKind>(), Arg.Any<IReadOnlyCollection<ChannelFavoriteKey>>()).Returns(true);
    }

    private IptvChannel Channel(string externalId, IptvChannelKind kind = IptvChannelKind.Tv) => new()
    {
        Id = Guid.NewGuid(),
        ExternalChannelId = externalId,
        Name = externalId,
        StreamUrl = "https://example.test/" + externalId,
        Kind = kind,
        PlaylistId = _playlist.Id,
        Playlist = _playlist
    };

    private void Favorites(IptvChannelKind kind, params IptvChannel[] channels) =>
        _repository.GetFavoriteChannelsAsync(_profileId, kind)
            .Returns(channels.Select((c, i) => new FavoriteChannel(c, DateTime.UtcNow.AddMinutes(i))).ToList());

    private static List<string> ArrayOf(string? json, string property) =>
        JsonNode.Parse(json ?? "{}")?[property]?.AsArray().Select(n => n?.GetValue<string>() ?? string.Empty).ToList() ?? new List<string>();

    [Fact]
    public async Task The_profile_favorites_replace_whatever_the_device_saved()
    {
        Favorites(IptvChannelKind.Tv, Channel("cnn.us"), Channel("bbc.uk"));

        var json = await _manager.MergeTvFavoritesAsync(_profileId, "{\"favoriteChannels\":[\"old.one\"],\"hideEmpty\":true}");

        ArrayOf(json, "favoriteChannels").Should().Equal("cnn.us", "bbc.uk");
        (JsonNode.Parse(json ?? "{}")?["hideEmpty"]?.GetValue<bool>()).Should().BeTrue();
    }

    [Fact]
    public async Task A_device_with_no_saved_prefs_still_gets_the_profile_favorites()
    {
        Favorites(IptvChannelKind.Tv, Channel("cnn.us"));

        var json = await _manager.MergeTvFavoritesAsync(_profileId, null);

        ArrayOf(json, "favoriteChannels").Should().Equal("cnn.us");
    }

    [Fact]
    public async Task Nothing_saved_and_no_favorites_stays_nothing()
    {
        (await _manager.MergeTvFavoritesAsync(_profileId, null)).Should().BeNull();
    }

    [Fact]
    public async Task An_old_provider_list_save_becomes_an_object_that_keeps_the_providers()
    {
        Favorites(IptvChannelKind.Tv, Channel("cnn.us"));

        var json = await _manager.MergeTvFavoritesAsync(_profileId, "[\"p1\",\"p2\"]");

        ArrayOf(json, "enabledProviders").Should().Equal("p1", "p2");
        ArrayOf(json, "favoriteChannels").Should().Equal("cnn.us");
    }

    [Fact]
    public async Task Saving_without_a_favorites_field_leaves_favorites_alone()
    {
        const string prefs = "{\"enabledProviders\":[\"p1\"]}";

        var stored = await _manager.SyncTvFavoritesAsync(_profileId, prefs);

        stored.Should().Be(prefs);
        await _repository.DidNotReceive().ReplaceFavoritesAsync(Arg.Any<Guid>(), Arg.Any<IptvChannelKind>(), Arg.Any<IReadOnlyCollection<ChannelFavoriteKey>>());
    }

    [Fact]
    public async Task Saving_favorites_stores_them_on_the_profile_and_not_in_the_device_prefs()
    {
        var cnn = Channel("CNN.us");
        _repository.FindChannelsByExternalIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), IptvChannelKind.Tv).Returns(new List<IptvChannel> { cnn });

        var stored = await _manager.SyncTvFavoritesAsync(_profileId, "{\"favoriteChannels\":[\"cnn.us\",\"cnn.us\",\"  \"],\"hideEmpty\":true}");

        await _repository.Received(1).FindChannelsByExternalIdsAsync(
            Arg.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { "cnn.us" })), IptvChannelKind.Tv);
        await _repository.Received(1).ReplaceFavoritesAsync(_profileId, IptvChannelKind.Tv,
            Arg.Is<IReadOnlyCollection<ChannelFavoriteKey>>(keys => keys.Single() == new ChannelFavoriteKey(_playlist.Id, "CNN.us")));
        await _notifier.Received(1).NotifyChannelFavoritesUpdatedAsync(_profileId);
        (JsonNode.Parse(stored)?["favoriteChannels"]).Should().BeNull();
        (JsonNode.Parse(stored)?["hideEmpty"]?.GetValue<bool>()).Should().BeTrue();
    }

    [Fact]
    public async Task An_empty_favorites_list_clears_them()
    {
        await _manager.SyncTvFavoritesAsync(_profileId, "{\"favoriteChannels\":[]}");

        await _repository.DidNotReceive().FindChannelsByExternalIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IptvChannelKind>());
        await _repository.Received(1).ReplaceFavoritesAsync(_profileId, IptvChannelKind.Tv,
            Arg.Is<IReadOnlyCollection<ChannelFavoriteKey>>(keys => keys.Count == 0));
    }

    [Fact]
    public async Task No_change_sends_no_notification()
    {
        _repository.ReplaceFavoritesAsync(Arg.Any<Guid>(), Arg.Any<IptvChannelKind>(), Arg.Any<IReadOnlyCollection<ChannelFavoriteKey>>()).Returns(false);

        await _manager.SyncTvFavoritesAsync(_profileId, "{\"favoriteChannels\":[]}");

        await _notifier.DidNotReceive().NotifyChannelFavoritesUpdatedAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Radio_favorites_are_station_ids_and_junk_is_ignored()
    {
        var station = Channel("station-uuid", IptvChannelKind.Radio);
        _repository.FindChannelsByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), IptvChannelKind.Radio).Returns(new List<IptvChannel> { station });

        var stored = await _manager.SyncRadioFavoritesAsync(_profileId, $"{{\"favoriteIds\":[\"{station.Id}\",\"not-a-guid\"],\"countryFilter\":\"US\"}}");

        await _repository.Received(1).FindChannelsByIdsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { station.Id })), IptvChannelKind.Radio);
        await _repository.Received(1).ReplaceFavoritesAsync(_profileId, IptvChannelKind.Radio,
            Arg.Is<IReadOnlyCollection<ChannelFavoriteKey>>(keys => keys.Single() == new ChannelFavoriteKey(_playlist.Id, "station-uuid")));
        (JsonNode.Parse(stored)?["favoriteIds"]).Should().BeNull();
        (JsonNode.Parse(stored)?["countryFilter"]?.GetValue<string>()).Should().Be("US");
    }

    [Fact]
    public async Task Radio_prefs_read_back_with_the_profile_station_ids()
    {
        var station = Channel("station-uuid", IptvChannelKind.Radio);
        Favorites(IptvChannelKind.Radio, station);

        var json = await _manager.MergeRadioFavoritesAsync(_profileId, "{\"hiddenIds\":[],\"countryFilter\":\"All\"}");

        ArrayOf(json, "favoriteIds").Should().Equal(station.Id.ToString());
    }

    [Fact]
    public async Task Unreadable_prefs_are_stored_as_they_came()
    {
        var stored = await _manager.SyncTvFavoritesAsync(_profileId, "{not json");

        stored.Should().Be("{not json");
        await _repository.DidNotReceive().ReplaceFavoritesAsync(Arg.Any<Guid>(), Arg.Any<IptvChannelKind>(), Arg.Any<IReadOnlyCollection<ChannelFavoriteKey>>());
    }
}
