using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Artwork;
using Vora.Application.Libraries;
using Vora.Application.Media;
using Vora.Application.Media.Dtos;
using Vora.Application.Metadata.Local;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Metadata;

public class LocalMetadataChangeFinderTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vora-nfo-changes-" + Guid.NewGuid().ToString("N"));
    private readonly ILibraryRepository _libraries = Substitute.For<ILibraryRepository>();
    private readonly IMediaRepository _media = Substitute.For<IMediaRepository>();
    private readonly Guid _library = Guid.NewGuid();
    private readonly List<LocalMediaSourceDto> _sources = [];
    private readonly LocalMetadataChangeFinder _finder;

    public LocalMetadataChangeFinderTests()
    {
        Directory.CreateDirectory(_directory);
        _media.GetLibraryLocalMediaSourcesAsync(_library).Returns(_sources);
        _finder = new LocalMetadataChangeFinder(_libraries, _media, NullLogger<LocalMetadataChangeFinder>.Instance, new Clock());
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private void Library(LocalMetadataMode mode, bool preferLocalArtwork = false, string artworkProvider = "tmdb_artwork") =>
        _libraries.GetProjectedByIdAsync(_library, Arg.Any<Expression<Func<MediaLibrary, LocalLibrarySettings>>>())
            .Returns(new LocalLibrarySettings(mode, preferLocalArtwork, "tmdb_metadata", artworkProvider));

    private string Write(string name, DateTime? written = null)
    {
        var path = Path.GetFullPath(Path.Combine(_directory, name));
        var parent = Path.GetDirectoryName(path);
        if (parent != null) Directory.CreateDirectory(parent);
        File.WriteAllText(path, "<movie><title>Title</title></movie>");
        if (written != null) File.SetLastWriteTimeUtc(path, written.Value);
        return path;
    }

    private LocalMediaSourceDto Movie(string folder, DateTime? refreshed, string? poster = null, params string[] locked)
    {
        var source = new LocalMediaSourceDto(Guid.NewGuid(), LocalMediaSourceDto.Movie, null, null, [Write($"{folder}/{folder}.mkv")])
        {
            LastMetadataRefresh = refreshed,
            PosterUrl = poster,
            LockedFields = locked
        };
        _sources.Add(source);
        return source;
    }

    private Task<LocalMetadataChanges> FindAsync(DateTime? since = null) =>
        _finder.FindAsync(_library, since, TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_library_with_both_switches_off_is_not_even_listed()
    {
        Library(LocalMetadataMode.Off);
        Movie("Alien (1979)", Now.AddDays(-1));
        Write("Alien (1979)/movie.nfo", Now.AddHours(-1));

        var changes = await FindAsync();

        changes.IsEmpty.Should().BeTrue();
        await _media.DidNotReceive().GetLibraryLocalMediaSourcesAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task An_nfo_saved_after_the_title_was_last_refreshed_is_read_again()
    {
        Library(LocalMetadataMode.MatchOnly);
        var edited = Movie("Alien (1979)", Now.AddDays(-1));
        var untouched = Movie("Heat (1995)", Now.AddDays(-1));
        var withoutNfo = Movie("Ronin (1998)", Now.AddDays(-1));
        Write("Alien (1979)/movie.nfo", Now.AddHours(-1));
        Write("Heat (1995)/movie.nfo", Now.AddDays(-30));

        var changes = await FindAsync();

        changes.Documents.Select(source => source.Id).Should().Equal(edited.Id);
        changes.Documents.Should().NotContain(source => source.Id == untouched.Id || source.Id == withoutNfo.Id);
    }

    [Fact]
    public async Task A_title_that_was_never_refreshed_is_left_to_the_normal_pass()
    {
        Library(LocalMetadataMode.Prefer);
        Movie("Alien (1979)", refreshed: null);
        Write("Alien (1979)/movie.nfo", Now.AddHours(-1));

        (await FindAsync()).Documents.Should().BeEmpty();
    }

    [Fact]
    public async Task An_nfo_dated_in_the_future_waits_until_that_time_has_passed()
    {
        Library(LocalMetadataMode.Prefer);
        Movie("Alien (1979)", Now.AddDays(-1));
        Write("Alien (1979)/movie.nfo", Now.AddDays(2));

        (await FindAsync()).Documents.Should().BeEmpty();
    }

    [Fact]
    public async Task After_the_setting_changes_every_title_with_an_nfo_is_read_once_whatever_its_date()
    {
        Library(LocalMetadataMode.Prefer);
        var old = Movie("Alien (1979)", Now.AddDays(-10));
        var alreadyDone = Movie("Heat (1995)", Now.AddMinutes(5));
        var withoutNfo = Movie("Ronin (1998)", Now.AddDays(-10));
        Write("Alien (1979)/movie.nfo", Now.AddDays(-300));
        Write("Heat (1995)/movie.nfo", Now.AddDays(-300));

        var changes = await FindAsync(since: Now);

        changes.Documents.Select(source => source.Id).Should().Equal(old.Id);
        changes.Documents.Should().NotContain(source => source.Id == alreadyDone.Id || source.Id == withoutNfo.Id);
    }

    [Theory]
    [InlineData(LocalMetadataMode.MatchOnly, false)]
    [InlineData(LocalMetadataMode.Prefer, true)]
    public async Task An_edited_episode_nfo_matters_only_when_the_library_prefers_nfo_details(LocalMetadataMode mode, bool expected)
    {
        Library(mode);
        var episodePath = Write("Show/Season 01/Show - S01E01 - Pilot.mkv");
        Write("Show/Season 01/Show - S01E01 - Pilot.nfo", Now.AddHours(-1));
        var showId = Guid.NewGuid();
        var episode = new LocalMediaSourceDto(Guid.NewGuid(), LocalMediaSourceDto.Episode, 1, 1, [episodePath])
        {
            TvShowId = showId,
            LastMetadataRefresh = Now.AddDays(-1)
        };
        _sources.Add(episode);

        var changes = await FindAsync();

        changes.Documents.Any(source => source.Id == episode.Id).Should().Be(expected);
    }

    [Fact]
    public async Task A_local_poster_the_title_is_not_using_yet_is_picked_up_when_the_library_prefers_local_images()
    {
        Library(LocalMetadataMode.Off, preferLocalArtwork: true);
        var movie = Movie("Alien (1979)", Now.AddDays(-1), poster: "https://image.tmdb.org/t/p/w500/online.jpg");
        Write("Alien (1979)/poster.jpg");

        (await FindAsync()).Artwork.Should().Equal(movie.Id);
    }

    [Fact]
    public async Task A_local_poster_already_in_use_is_left_alone_until_the_file_is_replaced()
    {
        Library(LocalMetadataMode.Off, preferLocalArtwork: true);
        var id = Guid.NewGuid();
        var video = Write("Alien (1979)/Alien (1979).mkv");
        var poster = Write("Alien (1979)/poster.jpg", Now.AddDays(-5));
        _sources.Add(new LocalMediaSourceDto(id, LocalMediaSourceDto.Movie, null, null, [video])
        {
            LastMetadataRefresh = Now.AddDays(-1),
            PosterUrl = LocalArtworkAddress.Build(id, poster)
        });

        (await FindAsync()).Artwork.Should().BeEmpty();

        File.SetLastWriteTimeUtc(poster, Now.AddHours(-1));

        (await FindAsync()).Artwork.Should().Equal(id);
    }

    [Fact]
    public async Task A_poster_chosen_and_locked_by_hand_is_never_swapped_for_a_local_one()
    {
        Library(LocalMetadataMode.Off, preferLocalArtwork: true);
        Movie("Alien (1979)", Now.AddDays(-1), "https://image.tmdb.org/t/p/w500/chosen.jpg", nameof(MediaItem.PosterUrl));
        Write("Alien (1979)/poster.jpg");

        (await FindAsync()).Artwork.Should().BeEmpty();
    }

    [Fact]
    public async Task A_title_still_pointing_at_a_local_image_that_was_deleted_is_refreshed()
    {
        Library(LocalMetadataMode.Off, preferLocalArtwork: true);
        var id = Guid.NewGuid();
        var video = Write("Alien (1979)/Alien (1979).mkv");
        _sources.Add(new LocalMediaSourceDto(id, LocalMediaSourceDto.Movie, null, null, [video])
        {
            LastMetadataRefresh = Now.AddDays(-1),
            PosterUrl = $"{LocalArtworkAddress.Prefix}{id}/poster.jpg?v=1"
        });

        (await FindAsync()).Artwork.Should().Equal(id);
    }

    [Fact]
    public async Task Turning_local_images_off_sends_titles_that_use_one_back_to_the_provider()
    {
        Library(LocalMetadataMode.Off, preferLocalArtwork: false);
        var id = Guid.NewGuid();
        var video = Write("Alien (1979)/Alien (1979).mkv");
        var poster = Write("Alien (1979)/poster.jpg");
        _sources.Add(new LocalMediaSourceDto(id, LocalMediaSourceDto.Movie, null, null, [video])
        {
            LastMetadataRefresh = Now.AddDays(-1),
            PosterUrl = LocalArtworkAddress.Build(id, poster)
        });

        (await FindAsync(since: Now)).Artwork.Should().Equal(id);
    }
}
