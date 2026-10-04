using Microsoft.EntityFrameworkCore;
using Vora.Application.Iptv;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class ChannelFavoriteRepositoryTests
{
    private static VoraDbContext NewContext(string name) =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("channel-favorites-" + name)
            .Options);

    private sealed class Fixture
    {
        public string Name { get; } = Guid.NewGuid().ToString("N");
        public UserProfile Profile { get; } = new() { Id = Guid.NewGuid(), Name = "Kid", UserId = Guid.NewGuid() };
        public IptvPlaylist Playlist { get; } = new() { Id = Guid.NewGuid(), Name = "Cable" };
        public IptvChannel Cnn { get; }
        public IptvChannel Bbc { get; }
        public IptvChannel Jazz { get; }

        public Fixture()
        {
            Cnn = Channel("cnn.us", IptvChannelKind.Tv);
            Bbc = Channel("bbc.uk", IptvChannelKind.Tv);
            Jazz = Channel("jazz-fm", IptvChannelKind.Radio);
        }

        private IptvChannel Channel(string externalId, IptvChannelKind kind) => new()
        {
            Id = Guid.NewGuid(),
            ExternalChannelId = externalId,
            Name = externalId,
            StreamUrl = "https://example.test/" + externalId,
            Kind = kind,
            PlaylistId = Playlist.Id
        };

        public ChannelFavoriteKey Key(IptvChannel channel) => new(channel.PlaylistId, channel.ExternalChannelId);

        public async Task<VoraDbContext> SeededAsync()
        {
            var db = NewContext(Name);
            db.AddRange(Profile, Playlist, Cnn, Bbc, Jazz);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return db;
        }
    }

    [Fact]
    public async Task Replacing_tv_favorites_leaves_radio_favorites_alone()
    {
        var f = new Fixture();
        await using var db = await f.SeededAsync();
        var repository = new ChannelFavoriteRepository(db);
        await repository.ReplaceFavoritesAsync(f.Profile.Id, IptvChannelKind.Radio, new[] { f.Key(f.Jazz) });
        await repository.ReplaceFavoritesAsync(f.Profile.Id, IptvChannelKind.Tv, new[] { f.Key(f.Cnn), f.Key(f.Bbc) });

        var changed = await repository.ReplaceFavoritesAsync(f.Profile.Id, IptvChannelKind.Tv, new[] { f.Key(f.Bbc) });

        changed.Should().BeTrue();
        await using var read = NewContext(f.Name);
        var reader = new ChannelFavoriteRepository(read);
        (await reader.GetFavoriteChannelsAsync(f.Profile.Id, IptvChannelKind.Tv)).Select(x => x.Channel.Name).Should().Equal("bbc.uk");
        (await reader.GetFavoriteChannelsAsync(f.Profile.Id, IptvChannelKind.Radio)).Select(x => x.Channel.Name).Should().Equal("jazz-fm");
    }

    [Fact]
    public async Task Saving_the_same_favorites_again_is_not_a_change()
    {
        var f = new Fixture();
        await using var db = await f.SeededAsync();
        var repository = new ChannelFavoriteRepository(db);
        await repository.ReplaceFavoritesAsync(f.Profile.Id, IptvChannelKind.Tv, new[] { f.Key(f.Cnn) });

        (await repository.ReplaceFavoritesAsync(f.Profile.Id, IptvChannelKind.Tv, new[] { f.Key(f.Cnn) })).Should().BeFalse();
    }

    [Fact]
    public async Task A_channel_that_leaves_the_playlist_and_comes_back_is_still_a_favorite()
    {
        var f = new Fixture();
        await using (var db = await f.SeededAsync())
        {
            await new ChannelFavoriteRepository(db).ReplaceFavoritesAsync(f.Profile.Id, IptvChannelKind.Tv, new[] { f.Key(f.Cnn) });
            db.Remove(db.IptvChannels.Single(c => c.Id == f.Cnn.Id));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = NewContext(f.Name))
        {
            (await new ChannelFavoriteRepository(db).GetFavoriteChannelsAsync(f.Profile.Id, IptvChannelKind.Tv)).Should().BeEmpty();
            db.Add(new IptvChannel { Id = Guid.NewGuid(), ExternalChannelId = "cnn.us", Name = "CNN", StreamUrl = "https://example.test/new", Kind = IptvChannelKind.Tv, PlaylistId = f.Playlist.Id });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var after = NewContext(f.Name);
        var favorites = await new ChannelFavoriteRepository(after).GetFavoriteChannelsAsync(f.Profile.Id, IptvChannelKind.Tv);
        favorites.Should().ContainSingle().Which.Channel.Name.Should().Be("CNN");
        favorites[0].Channel.Playlist.Name.Should().Be("Cable");
    }

    [Fact]
    public async Task Favorites_for_a_profile_that_does_not_exist_are_not_saved()
    {
        var f = new Fixture();
        await using var db = await f.SeededAsync();

        var changed = await new ChannelFavoriteRepository(db).ReplaceFavoritesAsync(Guid.NewGuid(), IptvChannelKind.Tv, new[] { f.Key(f.Cnn) });

        changed.Should().BeFalse();
        db.ProfileChannelFavorites.Should().BeEmpty();
    }

    [Fact]
    public async Task Channels_are_found_by_their_playlist_id_whatever_the_case()
    {
        var f = new Fixture();
        await using var db = await f.SeededAsync();

        var found = await new ChannelFavoriteRepository(db).FindChannelsByExternalIdsAsync(new[] { "CNN.US", "jazz-fm" }, IptvChannelKind.Tv);

        found.Select(c => c.Id).Should().Equal(f.Cnn.Id);
    }
}
