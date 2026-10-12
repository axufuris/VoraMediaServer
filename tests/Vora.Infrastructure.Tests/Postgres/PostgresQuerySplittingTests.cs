using Vora.Application.Media.SmartPlaylists;
using Vora.Application.Users.ViewModels;
using Vora.Domain.Entities.Actors;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Playlists;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests.Postgres;

public class PostgresQuerySplittingTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task Users_load_their_profiles_and_schedules()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var tag = Tag();
        var user = new User { Email = $"{tag}@example.com", DisplayName = "Andy " + tag };
        var adult = new UserProfile { Name = "Andy", UserId = user.Id };
        var kids = new UserProfile { Name = "Kids", UserId = user.Id };
        kids.AccessSchedules.Add(new ProfileAccessSchedule { DayOfWeek = DayOfWeek.Saturday, StartTime = TimeSpan.FromHours(8), EndTime = TimeSpan.FromHours(20) });
        db.AddRange(user, adult, kids);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = new UserRepository(db);
        var everyone = await repository.GetAllProjectedUsersAsync();
        var byId = await repository.GetProjectedUserByIdAsync(user.Id, UserVM.Projection);
        var byEmail = await repository.GetUserWithProfilesByEmailAsync(user.Email);

        everyone.Single(u => u.Id == user.Id).Profiles.Should().HaveCount(2);
        byId!.Profiles.Single(p => p.Name == "Kids").AccessSchedules.Should().ContainSingle();
        byEmail!.Profiles.Single(p => p.Name == "Kids").AccessSchedules.Should().ContainSingle();
    }

    [Fact]
    public async Task Playlist_summaries_load_their_posters_and_backdrops()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var tag = Tag();
        var library = new MediaLibrary { Name = "Movies " + tag, Type = LibraryType.Movie, FolderPaths = ["/movies"] };
        var user = new User { Email = $"{tag}@example.com", DisplayName = "Andy " + tag };
        var owner = new UserProfile { Name = "Andy", UserId = user.Id };
        var viewer = new UserProfile { Name = "Sarah", UserId = user.Id };
        var movies = Enumerable.Range(1, 3)
            .Select(n => new Movie { Title = $"Movie {n} {tag}", LibraryId = library.Id, PosterUrl = $"/posters/{tag}-{n}.jpg", BackgroundUrl = $"/backdrops/{tag}-{n}.jpg" })
            .ToList();
        var playlist = new Playlist { Name = "Friday " + tag, ProfileId = owner.Id, IsShared = true, SharedAt = DateTime.UtcNow };
        foreach (var (movie, order) in movies.Select((m, i) => (m, i)))
            playlist.Items.Add(new PlaylistItem { MediaItemId = movie.Id, Order = order });
        db.AddRange(library, user, owner, viewer);
        db.AddRange(movies);
        db.Add(playlist);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = new PlaylistRepository(db);
        var mine = await repository.GetPlaylistsAsync(owner.Id, PlaylistAccessFilter.Unrestricted);
        var shared = await repository.GetSharedByOthersAsync(viewer.Id, PlaylistAccessFilter.Unrestricted);
        var details = await repository.GetPlaylistDetailsAsync(playlist.Id, owner.Id, PlaylistAccessFilter.Unrestricted);

        mine.Single(p => p.Id == playlist.Id).PosterUrls.Should().HaveCount(3);
        shared.Single(p => p.Id == playlist.Id).BackdropUrls.Should().HaveCount(3);
        details!.PosterUrls.Should().StartWith($"/posters/{tag}-1.jpg");
    }

    [Fact]
    public async Task Titles_waiting_for_an_embedding_load_their_genres_and_cast()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var tag = Tag();
        var library = new MediaLibrary { Name = "Movies " + tag, Type = LibraryType.Movie, FolderPaths = ["/movies"] };
        var movie = new Movie { Title = "Arrival " + tag, LibraryId = library.Id };
        movie.Genres.Add(new Genre { Name = "Science Fiction " + tag });
        movie.Cast.Add(new MediaCastMember { Actor = new Actor { Name = "Amy Adams " + tag }, Order = 0 });
        db.AddRange(library, movie);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var waiting = await new OpenAiRecommendationRepository(db).GetMediaItemsMissingEmbeddingsAsync(int.MaxValue);

        var item = waiting.Single(m => m.Id == movie.Id);
        item.Genres.Should().ContainSingle();
        item.Cast.Should().ContainSingle();
    }

    [Fact]
    public async Task Merging_a_duplicate_show_moves_its_episodes_and_files()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var tag = Tag();
        var library = new MediaLibrary { Name = "Shows " + tag, Type = LibraryType.TvShow, FolderPaths = ["/shows"] };
        var tmdbId = "tmdb-" + tag;
        var keeper = new TvShow { Title = "Severance", LibraryId = library.Id, TmdbId = tmdbId, AddedAt = DateTime.UtcNow.AddDays(-2) };
        var duplicate = new TvShow { Title = "Severance", LibraryId = library.Id, TmdbId = tmdbId, AddedAt = DateTime.UtcNow.AddDays(-1) };
        var keeperSeason = new Season { Title = "Season 1", SeasonNumber = 1, TvShowId = keeper.Id, LibraryId = library.Id };
        var keeperEpisode = new Episode { Title = "Good News About Hell", EpisodeNumber = 1, SeasonId = keeperSeason.Id, LibraryId = library.Id };
        var duplicateSeason = new Season { Title = "Season 1", SeasonNumber = 1, TvShowId = duplicate.Id, LibraryId = library.Id };
        var duplicateEpisode = new Episode { Title = "Half Loop", EpisodeNumber = 2, SeasonId = duplicateSeason.Id, LibraryId = library.Id };
        var file = new MediaPart { FilePath = $"/shows/{tag}/s01e02.mkv", MediaItemId = duplicateEpisode.Id };
        db.AddRange(library, keeper, duplicate, keeperSeason, keeperEpisode, duplicateSeason, duplicateEpisode, file);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new MediaDedupeRepository(db).MergeDuplicateTvShowsAsync(library.Id);

        result.ShowsRemoved.Should().Be(1);
        result.KeeperShowIds.Should().ContainSingle().Which.Should().Be(keeper.Id);
    }
}
