using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Media;
using Vora.Domain.Entities.Actors;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests.Postgres;

// The detail page's lists are one SQL statement (MediaDetailsQuery), so these
// run on Postgres; the in-memory provider can't run raw SQL.
public class PostgresMediaDetailsTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private static int _nextId = Random.Shared.Next(1_000_000, 2_000_000);
    private static int NextId() => Interlocked.Increment(ref _nextId);
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Counter : DbCommandInterceptor
    {
        public int Count;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }

    private static MediaRepository Repository(VoraDbContext db) => new(NullLogger<MediaRepository>.Instance, db);

    private async Task<MediaDetailsVM?> DetailsAsync(Guid id, Guid? profileId = null)
    {
        await using var db = database.NewContext();
        return (await Repository(db).GetMediaDetailsAsync(id, profileId))?.Details;
    }

    // A random library, a profile part way through it, and every page in it
    // loaded both ways. Lists the old projection left in no particular order
    // are sorted before comparing.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task Every_page_shows_what_the_old_projection_showed(int seed)
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var world = await RandomLibrary.SeedAsync(database, seed);

        foreach (var id in world.ItemIds)
        {
            foreach (var profileId in new Guid?[] { world.ProfileId, null })
            {
                await using var db = database.NewContext();
                var expected = await MediaDetailsOracle.LoadAsync(db, id, profileId);
                var loaded = await Repository(db).GetMediaDetailsAsync(id, profileId);

                if (expected == null)
                {
                    loaded.Should().BeNull($"item {id} is missing");
                    continue;
                }

                loaded.Should().NotBeNull();
                Normalised(loaded!.Details).Should().Be(Normalised(expected), $"seed {seed}, {expected.Type} {expected.Title}, profile {profileId}");

                var showId = expected.Type == "TvShow" ? expected.Id : expected.TvShowId;
                var hasUpcoming = !string.IsNullOrWhiteSpace(expected.UpcomingEpisodesJson) && expected.UpcomingEpisodesJson != "[]";
                var held = showId is Guid show && hasUpcoming ? await Repository(db).GetHeldEpisodesForShowAsync(show) : [];
                loaded.HeldEpisodes.Should().BeEquivalentTo(held);
            }
        }
    }

    private static string Normalised(MediaDetailsVM vm)
    {
        vm.CollectionIds = vm.CollectionIds.Order().ToList();
        vm.Videos = vm.Videos.OrderBy(v => v.Type != "Trailer").ThenBy(v => v.VideoKey).ToList();
        vm.MediaParts = vm.MediaParts.OrderBy(p => p.Id).ToList();
        foreach (var part in vm.MediaParts)
        {
            part.VideoTracks = part.VideoTracks.OrderBy(t => t.Id).ToList();
            part.AudioTracks = part.AudioTracks.OrderBy(t => t.Id).ToList();
            part.SubtitleTracks = part.SubtitleTracks.OrderBy(t => t.Id).ToList();
        }
        return JsonSerializer.Serialize(vm, new JsonSerializerOptions { WriteIndented = true });
    }

    [Theory]
    [InlineData("Movie")]
    [InlineData("TvShow")]
    [InlineData("Season")]
    [InlineData("Episode")]
    public async Task A_page_is_two_queries_whatever_it_shows(string type)
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var tree = await SeedShowAsync();
        var id = type switch
        {
            "Movie" => await SeedMovieAsync(),
            "TvShow" => tree.ShowId,
            "Season" => tree.SeasonId,
            _ => tree.EpisodeId
        };

        var counter = new Counter();
        await using var db = database.NewContext(counter);
        var loaded = await Repository(db).GetMediaDetailsAsync(id, Guid.NewGuid());

        loaded.Should().NotBeNull();
        counter.Count.Should().Be(2);
    }

    [Fact]
    public async Task A_page_the_viewer_may_not_see_is_one_query_and_nothing()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var tree = await SeedShowAsync(showRating: "TV-MA");

        var counter = new Counter();
        await using var db = database.NewContext(counter);
        var loaded = await Repository(db).GetMediaDetailsAsync(tree.EpisodeId, Guid.NewGuid(),
            hasAllAccess: true, allowedLibs: [], allowedMovieRatings: [], allowedTvRatings: ["TV-PG"], blockUnrated: false);

        loaded.Should().BeNull();
        counter.Count.Should().Be(1);
    }

    // A season carries no genres, companies or cast of its own (ProcessTvSeasonsAsync
    // never writes them) and an episode only gets them when the provider returned
    // episode-level credits. Both inherit from the show so the credits block isn't
    // blank on those pages.

    private sealed record Tree(Guid ShowId, Guid SeasonId, Guid EpisodeId);

    private async Task<Tree> SeedShowAsync(bool showHasFacts = true, string? showRating = null, Action<Episode, Func<string, Actor>>? episode = null)
    {
        var tag = Tag();
        await using var db = database.NewContext();
        var library = new MediaLibrary { Name = "Shows " + tag, Type = LibraryType.TvShow, FolderPaths = ["/media/shows"] };
        var show = new TvShow { Title = "House of the Dragon " + tag, LibraryId = library.Id, ContentRating = showRating };
        var actors = new Dictionary<string, Actor>();
        Actor ActorNamed(string name) =>
            actors.TryGetValue(name, out var actor) ? actor : actors[name] = new Actor { Name = name, TmdbId = NextId() };

        if (showHasFacts)
        {
            show.Genres.Add(new Genre { Id = NextId(), Name = "Drama" });
            show.ProductionCompanies.Add(new Company { Id = NextId(), Name = "HBO" });
            show.Cast.Add(new MediaCastMember { Actor = ActorNamed("Greg Yaitanes"), Roles = MediaCastRole.Director, Order = 0 });
        }

        var season = new Season { Title = "Season 1", SeasonNumber = 1, LibraryId = library.Id, TvShowId = show.Id };
        var ep = new Episode { Title = "We Light the Way", EpisodeNumber = 5, LibraryId = library.Id, SeasonId = season.Id };
        episode?.Invoke(ep, ActorNamed);

        db.AddRange(library, show, season, ep);
        await db.SaveChangesAsync(Ct);
        return new Tree(show.Id, season.Id, ep.Id);
    }

    [Fact]
    public async Task An_episode_inherits_genres_studios_and_directors_from_the_show()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var tree = await SeedShowAsync();

        var vm = await DetailsAsync(tree.EpisodeId);

        vm!.Genres.Should().Equal("Drama");
        vm.Studios.Should().Equal("HBO");
        vm.Directors.Should().Equal("Greg Yaitanes");
    }

    [Fact]
    public async Task A_season_inherits_genres_studios_directors_and_cast_from_the_show()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var tree = await SeedShowAsync();

        var vm = await DetailsAsync(tree.SeasonId);

        vm!.Genres.Should().Equal("Drama");
        vm.Studios.Should().Equal("HBO");
        vm.Directors.Should().Equal("Greg Yaitanes");
        vm.Cast.Should().ContainSingle().Which.Name.Should().Be("Greg Yaitanes");
    }

    // The episode's own credits win: inheriting is a fallback, not an override.
    [Fact]
    public async Task An_episodes_own_facts_take_precedence_over_the_shows()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var tree = await SeedShowAsync(episode: (e, actor) =>
        {
            e.Genres.Add(new Genre { Id = NextId(), Name = "Action & Adventure" });
            e.ProductionCompanies.Add(new Company { Id = NextId(), Name = "Second Unit Pictures" });
            e.Cast.Add(new MediaCastMember { Actor = actor("Clare Kilner"), Roles = MediaCastRole.Director, Order = 0 });
        });

        var vm = await DetailsAsync(tree.EpisodeId);

        vm!.Genres.Should().Equal("Action & Adventure");
        vm.Studios.Should().Equal("Second Unit Pictures");
        vm.Directors.Should().Equal("Clare Kilner");
    }

    // Why Directors is separate from Cast: the episode has a guest cast but no
    // directing credit, so the director inherits while the cast row keeps
    // showing the guest actors.
    [Fact]
    public async Task An_episode_with_guest_cast_but_no_director_still_inherits_the_director()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var tree = await SeedShowAsync(episode: (e, actor) =>
            e.Cast.Add(new MediaCastMember { Actor = actor("Guest Star"), Roles = MediaCastRole.Actor, Order = 0 }));

        var vm = await DetailsAsync(tree.EpisodeId);

        vm!.Directors.Should().Equal("Greg Yaitanes");
        vm.Cast.Should().ContainSingle().Which.Name.Should().Be("Guest Star");
    }

    [Fact]
    public async Task Nothing_is_invented_when_the_show_has_no_facts_either()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var tree = await SeedShowAsync(showHasFacts: false);

        var vm = await DetailsAsync(tree.EpisodeId);

        vm!.Genres.Should().BeEmpty();
        vm.Studios.Should().BeEmpty();
        vm.Directors.Should().BeEmpty();
        vm.Cast.Should().BeEmpty();
    }

    [Fact]
    public async Task A_movie_is_unaffected_by_the_fallback()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var movie = await SeedMovieAsync();

        var vm = await DetailsAsync(movie);

        vm!.Genres.Should().Equal("Action");
        vm.Studios.Should().BeEmpty();
        vm.Directors.Should().BeEmpty();
    }

    private async Task<Guid> SeedMovieAsync(Action<MediaPart>? configurePart = null)
    {
        var tag = Tag();
        await using var db = database.NewContext();
        var library = new MediaLibrary { Name = "Movies " + tag, Type = LibraryType.Movie, FolderPaths = ["/movies1080"] };
        var movie = new Movie { Title = "Man of War " + tag, LibraryId = library.Id };
        movie.Genres.Add(new Genre { Id = NextId(), Name = "Action" });
        var part = new MediaPart
        {
            FilePath = $"/movies1080/Man of War (2026)/Man of War (2026) {tag} [Remux-1080p][DTS-HD MA 5.1][AVC]-Aisha.mkv",
            Container = "mkv",
            Resolution = "1080p",
            VersionName = "Remux",
            Duration = new TimeSpan(1, 50, 38),
            FileSizeBytes = 9_266_000_000,
            OverallBitrate = 11_167_000,
        };
        part.VideoTracks.Add(new MediaVideoTrack { Codec = "hevc", Profile = "main 10", BitDepth = 10, Bitrate = 9_759_000, IsDefault = true });
        part.SubtitleTracks.Add(new MediaSubtitleTrack { Codec = "subrip", Language = "eng", ExternalFilePath = "/movies1080/Man of War (2026)/Man of War (2026).en.srt", IsDownloaded = true });
        part.SubtitleTracks.Add(new MediaSubtitleTrack { Codec = "hdmv_pgs_subtitle", Language = "eng", StreamIndex = 3 });
        configurePart?.Invoke(part);
        movie.MediaParts.Add(part);

        db.AddRange(library, movie);
        await db.SaveChangesAsync(Ct);
        return movie.Id;
    }

    [Fact]
    public async Task A_part_carries_its_container_version_duration_and_bitrates()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var movie = await SeedMovieAsync();

        var part = (await DetailsAsync(movie))!.MediaParts.Single();

        part.Container.Should().Be("mkv");
        part.VersionName.Should().Be("Remux");
        part.DurationSeconds.Should().Be(6638);
        part.BitrateKbps.Should().Be(11167);
        part.VideoTracks.Single().BitrateKbps.Should().Be(9759);
    }

    [Fact]
    public async Task A_part_with_no_analysed_duration_reports_none()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var movie = await SeedMovieAsync(p => p.Duration = null);

        (await DetailsAsync(movie))!.MediaParts.Single().DurationSeconds.Should().BeNull();
    }

    [Fact]
    public async Task A_subtitle_says_whether_it_is_a_sidecar_and_whether_vora_downloaded_it()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var movie = await SeedMovieAsync();

        var subtitles = (await DetailsAsync(movie))!.MediaParts.Single().SubtitleTracks;

        subtitles.Should().ContainSingle(s => s.IsExternal && s.IsDownloaded && s.Codec == "subrip");
        subtitles.Should().ContainSingle(s => !s.IsExternal && !s.IsDownloaded && s.Codec == "hdmv_pgs_subtitle");
    }

    [Fact]
    public async Task Embedded_subtitles_come_in_stream_order_with_their_positions_and_a_sidecar_last_with_none()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var forcedEnglish = Guid.NewGuid();
        var french = Guid.NewGuid();
        var movie = await SeedMovieAsync(p =>
        {
            p.SubtitleTracks.Add(new MediaSubtitleTrack { Id = french, Codec = "subrip", Language = "fra", StreamIndex = 7 });
            p.SubtitleTracks.Add(new MediaSubtitleTrack { Id = forcedEnglish, Codec = "hdmv_pgs_subtitle", Language = "eng", StreamIndex = 5, IsForced = true });
        });

        var subtitles = (await DetailsAsync(movie))!.MediaParts.Single().SubtitleTracks;

        subtitles.Select(s => s.EmbeddedPosition).Should().Equal(0, 1, 2, null);
        subtitles[1].Id.Should().Be(forcedEnglish);
        subtitles[2].Id.Should().Be(french);
        subtitles[3].IsExternal.Should().BeTrue();
    }

    [Fact]
    public async Task Audio_tracks_come_in_stream_order_with_their_positions()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var english = Guid.NewGuid();
        var commentary = Guid.NewGuid();
        var japanese = Guid.NewGuid();
        var movie = await SeedMovieAsync(p =>
        {
            p.AudioTracks.Add(new MediaAudioTrack { Id = japanese, Codec = "truehd", Language = "jpn", StreamIndex = 4 });
            p.AudioTracks.Add(new MediaAudioTrack { Id = english, Codec = "eac3", Language = "eng", StreamIndex = 1, IsDefault = true });
            p.AudioTracks.Add(new MediaAudioTrack { Id = commentary, Codec = "aac", Language = "eng", StreamIndex = 2 });
        });

        var audio = (await DetailsAsync(movie))!.MediaParts.Single().AudioTracks;

        audio.Select(a => a.Id).Should().Equal(english, commentary, japanese);
        audio.Select(a => a.EmbeddedPosition).Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task A_season_counts_what_the_profile_has_left_to_watch()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var tag = Tag();
        Guid seasonId, showId, profileId, watched;
        await using (var db = database.NewContext())
        {
            var library = new MediaLibrary { Name = "Shows " + tag, Type = LibraryType.TvShow, FolderPaths = ["/t"] };
            var user = new User { Email = $"{tag}@example.com", DisplayName = tag };
            var profile = new UserProfile { Name = "Viewer", UserId = user.Id };
            var show = new TvShow { Title = "Haunted Hotel " + tag, LibraryId = library.Id };
            var season = new Season { Title = "Season 2", SeasonNumber = 2, LibraryId = library.Id, TvShowId = show.Id };
            var episodes = Enumerable.Range(1, 3)
                .Select(n => new Episode { Title = $"Episode {n}", EpisodeNumber = n, LibraryId = library.Id, SeasonId = season.Id })
                .ToList();
            db.AddRange(library, user, profile, show, season);
            db.AddRange(episodes);
            db.Add(new UserMediaState { ProfileId = profile.Id, MediaItemId = episodes[0].Id, IsPlayed = true });
            db.Add(new UserMediaState { ProfileId = profile.Id, MediaItemId = episodes[1].Id, ResumePositionSeconds = 300 });
            db.Add(new UserMediaRating { ProfileId = profile.Id, MediaItemId = season.Id, Rating = 8.5m });
            await db.SaveChangesAsync(Ct);
            (seasonId, showId, profileId, watched) = (season.Id, show.Id, profile.Id, episodes[0].Id);
        }

        var season2 = await DetailsAsync(seasonId, profileId);
        var show2 = await DetailsAsync(showId, profileId);

        season2!.UnplayedItemCount.Should().Be(2);
        season2.Episodes.Select(e => e.IsPlayed).Should().Equal(true, false, false);
        season2.Episodes[1].ResumePositionSeconds.Should().Be(300);
        season2.MyRating.Should().Be(8.5m);
        show2!.UnplayedItemCount.Should().Be(2);
        show2.Seasons.Single().Should().BeEquivalentTo(new { UnplayedItemCount = 2, IsPlayed = false, MyRating = 8.5m, EpisodeCount = 3 });
        (await DetailsAsync(seasonId))!.UnplayedItemCount.Should().BeNull("there is no profile to count for");
    }

    // Seeded at random: shows with missing seasons and episodes, double
    // episodes, credits on some episodes and not others, extras, trailers,
    // markers, files with tracks, and a profile part way through it all.
    private sealed class RandomLibrary
    {
        public List<Guid> ItemIds { get; } = new();
        public Guid ProfileId { get; private set; }

        private static readonly string[] VideoTypes = ["Trailer", "Teaser", "Featurette", "Clip"];
        private static readonly MediaCastRole[] Roles = [MediaCastRole.Actor, MediaCastRole.Director, MediaCastRole.Actor | MediaCastRole.Director, MediaCastRole.Writer | MediaCastRole.Producer];

        public static async Task<RandomLibrary> SeedAsync(PostgresDatabase database, int seed)
        {
            var rng = new Random(seed);
            var tag = Tag();
            var world = new RandomLibrary();
            await using var db = database.NewContext();

            var movies = new MediaLibrary { Name = "Movies " + tag, Type = LibraryType.Movie, FolderPaths = ["/m"] };
            var tv = new MediaLibrary { Name = "TV " + tag, Type = LibraryType.TvShow, FolderPaths = ["/t"] };
            var user = new User { Email = $"{tag}@example.com", DisplayName = tag };
            var profile = new UserProfile { Name = "Viewer", UserId = user.Id };
            world.ProfileId = profile.Id;
            db.AddRange(movies, tv, user, profile);

            var genres = Enumerable.Range(0, 6).Select(i => new Genre { Id = NextId(), Name = $"{(char)('A' + rng.Next(26))}enre {i} {tag}" }).ToList();
            var companies = Enumerable.Range(0, 4).Select(i => new Company { Id = NextId(), Name = $"{(char)('A' + rng.Next(26))}tudio {i} {tag}" }).ToList();
            var actors = Enumerable.Range(0, 10).Select(i => new Actor
            {
                Name = $"Actor {i} {tag}",
                TmdbId = NextId(),
                ProfileImageUrl = rng.Next(3) == 0 ? null : $"/people/{tag}-{i}.jpg"
            }).ToList();
            var collections = Enumerable.Range(0, 2).Select(i => new Collection { Title = $"Collection {i} {tag}" }).ToList();
            db.AddRange(collections);

            List<T> Some<T>(List<T> pool, int max) => pool.OrderBy(_ => rng.Next()).Take(rng.Next(max + 1)).ToList();

            void Decorate(MediaItem item, int richness)
            {
                foreach (var g in Some(genres, richness)) item.Genres.Add(g);
                foreach (var c in Some(companies, richness)) item.ProductionCompanies.Add(c);
                var cast = Some(actors, richness + 1);
                var orders = Enumerable.Range(0, cast.Count).OrderBy(_ => rng.Next()).ToList();
                foreach (var (actor, i) in cast.Select((a, i) => (a, i)))
                    item.Cast.Add(new MediaCastMember { Actor = actor, Order = orders[i], Roles = Roles[rng.Next(Roles.Length)], CharacterName = rng.Next(2) == 0 ? null : $"Role {i}" });
                foreach (var c in Some(collections, 1)) item.Collections.Add(c);
                for (var i = rng.Next(richness + 1); i > 0; i--)
                    item.Videos.Add(new MediaVideo { VideoKey = $"{tag}-{Guid.NewGuid():N}", Name = $"Video {i}", Site = "YouTube", Type = VideoTypes[rng.Next(VideoTypes.Length)], IsOfficial = rng.Next(2) == 0 });
                for (var i = rng.Next(richness + 1); i > 0; i--)
                {
                    var extra = new MediaExtra { Title = $"Extra {i}", ExtraType = (MediaExtraType)rng.Next(10), MediaItemId = item.Id };
                    if (rng.Next(2) == 0) extra.Parts.Add(new MediaPart { FilePath = $"/x/{tag}/{Guid.NewGuid():N}.mp4", Container = rng.Next(2) == 0 ? "mp4" : "mkv" });
                    item.Extras.Add(extra);
                }
                var starts = Enumerable.Range(0, 20).OrderBy(_ => rng.Next()).Take(rng.Next(4)).ToList();
                foreach (var (start, i) in starts.Select((s, i) => (s, i)))
                    item.Markers.Add(new MediaItemMarker { Type = (MarkerType)rng.Next(5), Start = TimeSpan.FromSeconds(start * 30 + 0.25), End = TimeSpan.FromSeconds(start * 30 + 12.5), Order = i });
                if (rng.Next(2) == 0)
                    item.Analysis = new MediaItemAnalysis { MediaItemId = item.Id, Duration = TimeSpan.FromSeconds(rng.Next(600, 9000) + 0.5) };
            }

            void Files(MediaItem item, int max)
            {
                for (var p = rng.Next(max + 1); p > 0; p--)
                {
                    var part = new MediaPart
                    {
                        FilePath = $"/f/{tag}/{Guid.NewGuid():N}.mkv",
                        Container = "mkv",
                        Resolution = rng.Next(2) == 0 ? "1080p" : "4K",
                        VersionName = rng.Next(2) == 0 ? null : "Remux",
                        Edition = rng.Next(3) == 0 ? "Director's Cut" : null,
                        Duration = rng.Next(3) == 0 ? null : TimeSpan.FromSeconds(rng.Next(600, 9000) + 0.75),
                        FileSizeBytes = rng.Next(3) == 0 ? null : rng.NextInt64(1_000_000, 50_000_000_000),
                        OverallBitrate = rng.Next(3) == 0 ? null : rng.NextInt64(500_000, 80_000_000),
                    };
                    part.VideoTracks.Add(new MediaVideoTrack { StreamIndex = 0, Codec = "hevc", Bitrate = rng.Next(2) == 0 ? null : rng.NextInt64(500_000, 60_000_000), Width = 1920, Height = 1080, IsDefault = true });
                    if (rng.Next(3) == 0) part.VideoTracks.Add(new MediaVideoTrack { StreamIndex = 9, Codec = "mjpeg" });
                    var streams = Enumerable.Range(1, 8).OrderBy(_ => rng.Next()).ToList();
                    for (var a = rng.Next(4); a > 0; a--)
                        part.AudioTracks.Add(new MediaAudioTrack { StreamIndex = streams[a], Codec = "eac3", Language = rng.Next(2) == 0 ? "eng" : "jpn", Channels = 6, IsDefault = a == 1 });
                    for (var s = rng.Next(4); s > 0; s--)
                        part.SubtitleTracks.Add(new MediaSubtitleTrack { StreamIndex = streams[4 + s - 1], Codec = "subrip", Language = "eng", IsForced = rng.Next(4) == 0, IsHearingImpaired = rng.Next(4) == 0 });
                    if (rng.Next(3) == 0)
                        part.SubtitleTracks.Add(new MediaSubtitleTrack { Codec = "subrip", Language = "fra", ExternalFilePath = $"/f/{tag}/{Guid.NewGuid():N}.fr.srt", IsDownloaded = rng.Next(2) == 0 });
                    item.MediaParts.Add(part);
                }
            }

            DateTime? Missing() => rng.Next(5) == 0 ? DateTime.UtcNow.AddDays(-3) : null;
            decimal? AdminRating() => rng.Next(3) == 0 ? rng.Next(0, 21) / 2m : null;

            var items = new List<MediaItem>();
            for (var m = 0; m < 3; m++)
            {
                var movie = new Movie { Title = $"Movie {m} {tag}", LibraryId = movies.Id, MissingSince = m == 2 ? Missing() : null, ServerAdminRating = AdminRating(), ReleaseDate = new DateOnly(2000 + rng.Next(26), 1 + rng.Next(12), 1 + rng.Next(28)) };
                Decorate(movie, 3);
                Files(movie, 2);
                items.Add(movie);
            }

            string[] upcoming = ["[]", "[{\"seasonNumber\":9,\"episodeNumber\":1,\"title\":\"Soon\",\"airDate\":\"2027-01-01\"}]"];
            for (var s = 0; s < 2; s++)
            {
                var show = new TvShow { Title = $"Show {s} {tag}", LibraryId = tv.Id, UpcomingEpisodesJson = rng.Next(3) == 0 ? null : upcoming[rng.Next(2)], ServerAdminRating = AdminRating() };
                Decorate(show, 3);
                items.Add(show);
                foreach (var number in Enumerable.Range(0, 4).OrderBy(_ => rng.Next()).Take(1 + rng.Next(3)))
                {
                    var season = new Season { Title = $"Season {number}", SeasonNumber = number, TvShowId = show.Id, LibraryId = tv.Id, MissingSince = Missing(), ServerAdminRating = AdminRating(), PosterUrl = rng.Next(2) == 0 ? null : $"/s/{tag}-{number}.jpg" };
                    if (rng.Next(4) == 0) Decorate(season, 1);
                    items.Add(season);
                    var next = 1;
                    for (var e = rng.Next(5) == 0 ? 0 : 1 + rng.Next(5); e > 0; e--)
                    {
                        var episode = new Episode
                        {
                            Title = $"S{number}E{next}",
                            EpisodeNumber = next,
                            EndEpisodeNumber = rng.Next(5) == 0 ? next + 1 : null,
                            SeasonId = season.Id,
                            LibraryId = tv.Id,
                            MissingSince = Missing(),
                            ReleaseDate = rng.Next(4) == 0 ? null : new DateOnly(2026, 1 + rng.Next(12), 1 + rng.Next(28)),
                            Overview = rng.Next(2) == 0 ? null : "Something happens.",
                            ServerAdminRating = AdminRating(),
                        };
                        next = (episode.EndEpisodeNumber ?? next) + 1;
                        if (rng.Next(3) == 0) Decorate(episode, 2);
                        else if (rng.Next(2) == 0) episode.Analysis = new MediaItemAnalysis { MediaItemId = episode.Id, Duration = TimeSpan.FromSeconds(rng.Next(1200, 3600) + 0.5) };
                        Files(episode, 1);
                        items.Add(episode);
                    }
                }
            }

            db.AddRange(items);
            foreach (var item in items.Where(i => i is Movie or Episode && rng.Next(2) == 0))
                db.Add(new UserMediaState { ProfileId = profile.Id, MediaItemId = item.Id, IsPlayed = rng.Next(2) == 0, ResumePositionSeconds = rng.Next(3) == 0 ? 0 : rng.Next(60, 5000) + 0.125 });
            foreach (var item in items.Where(_ => rng.Next(3) == 0))
                db.Add(new UserMediaRating { ProfileId = profile.Id, MediaItemId = item.Id, Rating = rng.Next(0, 21) / 2m });
            await db.SaveChangesAsync(Ct);

            world.ItemIds.AddRange(items.Select(i => i.Id));
            return world;
        }
    }
}
