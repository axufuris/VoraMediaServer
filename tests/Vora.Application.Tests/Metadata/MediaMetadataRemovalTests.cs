using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Vora.Application.Artwork;
using Vora.Application.Media;
using Vora.Application.Media.Requests;
using Vora.Application.Metadata;
using Vora.Application.Metadata.Local;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Actors;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Metadata;

public class MediaMetadataRemovalTests
{
    private static readonly Guid LibraryId = Guid.Parse("0f5f1c8e-1d7e-4a43-9a63-3d2a6c1a8b11");

    private readonly IMediaRepository _repository = Substitute.For<IMediaRepository>();
    private readonly ITaskQueueManager _taskQueue = Substitute.For<ITaskQueueManager>();
    private readonly ILocalMediaReader _localMedia = Substitute.For<ILocalMediaReader>();
    private readonly IMediaArtworkRepository _artwork = Substitute.For<IMediaArtworkRepository>();

    public MediaMetadataRemovalTests()
    {
        _artwork.GetUploadedArtworkUrlsAsync(Arg.Any<IReadOnlyCollection<Guid>>()).Returns(new HashSet<string>());
        _repository.GetShowChildrenForMetadataResetAsync(Arg.Any<Guid>()).Returns(new List<MediaItem>());
    }

    private MediaMatchManager Manager() =>
        new(_repository, [], Substitute.For<IMediaDedupeManager>(), _taskQueue, _localMedia, _artwork, NullLogger<MediaMatchManager>.Instance);

    private static Movie WrongMatch() => new()
    {
        Title = "Home Alone",
        LibraryId = LibraryId,
        Library = new MediaLibrary { Name = "Movies" },
        TmdbId = "771",
        ImdbId = "tt0099785",
        Overview = "An eight-year-old is left behind.",
        PosterUrl = "/api/artwork/custom/home-alone_overlay_1.jpg",
        OriginalPosterUrl = "https://image.tmdb.org/t/p/original/home-alone.jpg",
        LastMetadataRefresh = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        LockedFields = [MediaMatchManager.MatchLock],
        Genres = [new Genre { Name = "Comedy" }],
        Cast = [new MediaCastMember { CharacterName = "Kevin" }],
        Videos = [new MediaVideo { VideoKey = "trailer", Name = "Trailer", Site = "YouTube", Type = "Trailer" }],
    };

    private void Holds(MediaItem item) => _repository.GetForMetadataSyncAsync(item.Id).Returns(item);

    [Fact]
    public async Task Removing_metadata_leaves_the_name_from_the_file_and_keeps_it_unmatched()
    {
        var movie = WrongMatch();
        Holds(movie);
        _localMedia.ReadNamingAsync(movie.Id).Returns(new LocalNaming("Christmas 2019", 2019, ExternalIdSet.Empty));

        await Manager().RemoveMetadataAsync(movie.Id);

        movie.Title.Should().Be("Christmas 2019");
        movie.ReleaseDate.Should().Be(new DateOnly(2019, 1, 1));
        movie.TmdbId.Should().BeNull();
        movie.ImdbId.Should().BeNull();
        movie.Overview.Should().BeNull();
        movie.PosterUrl.Should().BeNull();
        movie.OriginalPosterUrl.Should().BeNull();
        movie.Genres.Should().BeEmpty();
        movie.IsLocked(MediaMatchManager.UnmatchedLock).Should().BeTrue();
        movie.IsLocked(MediaMatchManager.MatchLock).Should().BeFalse();
    }

    [Fact]
    public async Task Removing_metadata_deletes_the_cast_trailers_and_provider_artwork_then_refreshes_from_local_files()
    {
        var movie = WrongMatch();
        Holds(movie);
        var cast = movie.Cast.ToList();
        var videos = movie.Videos.ToList();

        await Manager().RemoveMetadataAsync(movie.Id);

        await _repository.Received(1).RemoveMediaCastMembersAsync(Arg.Is<IEnumerable<MediaCastMember>>(removed => removed.SequenceEqual(cast)));
        await _repository.Received(1).RemoveMediaVideosAsync(Arg.Is<IEnumerable<MediaVideo>>(removed => removed.SequenceEqual(videos)));
        await _repository.Received(1).SaveChangesAsync();
        await _artwork.Received(1).ClearProviderArtworkAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { movie.Id })));
        _taskQueue.Received(1).QueueRefreshMediaItemMetadata(movie.Id, "Home Alone", false, LibraryId);
    }

    [Fact]
    public async Task Removing_metadata_keeps_what_the_admin_locked_and_an_uploaded_poster()
    {
        var movie = WrongMatch();
        movie.LockField(nameof(movie.Overview));
        movie.PosterUrl = "/api/artwork/custom/upload-1_overlay_2.jpg";
        movie.OriginalPosterUrl = "/api/artwork/custom/upload-1.jpg";
        Holds(movie);
        _artwork.GetUploadedArtworkUrlsAsync(Arg.Any<IReadOnlyCollection<Guid>>()).Returns(new HashSet<string> { "/api/artwork/custom/upload-1.jpg" });

        await Manager().RemoveMetadataAsync(movie.Id);

        movie.Overview.Should().Be("An eight-year-old is left behind.");
        movie.PosterUrl.Should().Be("/api/artwork/custom/upload-1_overlay_2.jpg");
        movie.OriginalPosterUrl.Should().Be("/api/artwork/custom/upload-1.jpg");
    }

    [Fact]
    public async Task Removing_a_shows_metadata_names_its_seasons_and_episodes_from_the_files()
    {
        var show = new TvShow
        {
            Title = "The Office (US)",
            LibraryId = LibraryId,
            Library = new MediaLibrary { Name = "Shows" },
            TvdbId = "73244",
        };
        var season = new Season { Title = "The First Season", SeasonNumber = 1, TvShowId = show.Id, Overview = "Michael manages.", TvShow = show };
        var named = new Episode
        {
            Title = "Pilot",
            EpisodeNumber = 1,
            Season = season,
            SeasonId = season.Id,
            Overview = "A documentary crew arrives.",
            MediaParts = [new MediaPart { FilePath = "/tv/The Office/Season 01/The Office - S01E01 - Welcome.mkv" }],
        };
        var bare = new Episode
        {
            Title = "Diversity Day",
            EpisodeNumber = 2,
            Season = season,
            SeasonId = season.Id,
            MediaParts = [new MediaPart { FilePath = "/tv/The Office/Season 01/S01E02.mkv" }],
        };
        Holds(show);
        _repository.GetShowChildrenForMetadataResetAsync(show.Id).Returns(new List<MediaItem> { season, named, bare });
        _localMedia.ReadNamingAsync(show.Id).Returns(new LocalNaming("The Office", null, ExternalIdSet.Empty));

        await Manager().RemoveMetadataAsync(show.Id);

        show.Title.Should().Be("The Office");
        show.TvdbId.Should().BeNull();
        season.Title.Should().Be("Season 1");
        season.Overview.Should().BeNull();
        named.Title.Should().Be("Welcome");
        named.Overview.Should().BeNull();
        bare.Title.Should().Be("The Office - S01E02");
        await _artwork.Received(1).ClearProviderArtworkAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 4));
    }

    [Fact]
    public async Task Only_movies_and_shows_can_have_their_metadata_removed()
    {
        var episode = new Episode { Title = "Pilot", EpisodeNumber = 1 };
        Holds(episode);

        var remove = () => Manager().RemoveMetadataAsync(episode.Id);

        await remove.Should().ThrowAsync<InvalidOperationException>();
        await _repository.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task Matching_automatically_lifts_the_unmatched_lock_and_looks_the_title_up_again()
    {
        var movie = new Movie { Title = "Christmas 2019", LibraryId = LibraryId, Library = new MediaLibrary { Name = "Movies" }, LockedFields = [MediaMatchManager.UnmatchedLock] };
        Holds(movie);

        await Manager().ClearAsync(movie.Id);

        movie.IsLocked(MediaMatchManager.UnmatchedLock).Should().BeFalse();
        _taskQueue.Received(1).QueueRefreshMediaItemMetadata(movie.Id, "Christmas 2019", false, LibraryId);
    }

    [Fact]
    public async Task Fixing_the_match_of_an_unmatched_title_lifts_the_unmatched_lock()
    {
        var movie = new Movie { Title = "Christmas 2019", LibraryId = LibraryId, Library = new MediaLibrary { Name = "Movies" }, LockedFields = [MediaMatchManager.UnmatchedLock] };
        Holds(movie);

        await Manager().ApplyAsync(movie.Id, new ApplyMediaMatchRequest { Source = "tmdb", ExternalId = "771" }, TestContext.Current.CancellationToken);

        movie.IsLocked(MediaMatchManager.UnmatchedLock).Should().BeFalse();
        movie.IsLocked(MediaMatchManager.MatchLock).Should().BeTrue();
        movie.TmdbId.Should().Be("771");
    }
}
