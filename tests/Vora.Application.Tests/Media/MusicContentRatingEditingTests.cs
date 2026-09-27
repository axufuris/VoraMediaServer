using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Media.Requests;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Media;

// A parent who corrects a song's rating expects it to stay corrected. A hand
// edit is locked, so neither a rescan's file tag nor the provider lookup puts
// the old answer back.
public class MusicContentRatingEditingTests
{
    private readonly IMusicRepository _repository = Substitute.For<IMusicRepository>();
    private readonly MusicManager _manager;

    public MusicContentRatingEditingTests()
    {
        _manager = new MusicManager(
            _repository,
            Substitute.For<IUserRepository>(),
            Substitute.For<IUserMediaStateRepository>(),
            Array.Empty<IMusicArtworkProvider>(),
            Array.Empty<ILyricsProvider>(),
            Array.Empty<IListeningDataProvider>(),
            Substitute.For<IClientNotifier>(),
            Options.Create(new StoragePathsOptions()),
            new NullTaskProgressReporter(),
            NullLogger<MusicManager>.Instance);
    }

    private Track GivenTrack(string? rating, string? provider = null, params string[] locked)
    {
        var track = new Track { Id = Guid.NewGuid(), Title = "Without Me", ContentRating = rating, ContentRatingProvider = provider, LockedFields = locked.ToList() };
        _repository.GetTrackForUpdateAsync(track.Id).Returns(track);
        return track;
    }

    private Task<bool> Save(Track track, string? rating, params string[] locked) =>
        _manager.UpdateTrackAsync(track.Id, new UpdateTrackRequest { Title = track.Title, ContentRating = rating, LockedFields = locked.ToList() });

    [Fact]
    public async Task A_hand_edit_sets_the_rating_and_locks_it()
    {
        var track = GivenTrack("Clean", provider: "deezer_content_ratings");

        await Save(track, "Explicit");

        track.ContentRating.Should().Be("Explicit");
        track.ContentRatingProvider.Should().BeNull();
        track.IsLocked(nameof(Track.ContentRating)).Should().BeTrue();
        MusicContentRating.SourceOf(track).Should().Be(MusicContentRatingSource.Manual);
    }

    // Otherwise the provider would fill it straight back in.
    [Fact]
    public async Task Clearing_the_rating_by_hand_is_locked_too()
    {
        var track = GivenTrack("Explicit");

        await Save(track, "");

        track.ContentRating.Should().BeNull();
        track.IsLocked(nameof(Track.ContentRating)).Should().BeTrue();
    }

    // The lock keeps automation off a hand-set rating, not the admin.
    [Fact]
    public async Task A_second_hand_edit_is_not_blocked_by_the_first_ones_lock()
    {
        var track = GivenTrack("Clean", null, nameof(Track.ContentRating));

        await Save(track, "Explicit", nameof(Track.ContentRating));

        track.ContentRating.Should().Be("Explicit");
    }

    // Editing the title must not quietly lock a rating that came from the file.
    [Fact]
    public async Task Saving_other_fields_leaves_an_unchanged_rating_unlocked()
    {
        var track = GivenTrack("Explicit");

        await Save(track, "Explicit");

        track.IsLocked(nameof(Track.ContentRating)).Should().BeFalse();
        MusicContentRating.SourceOf(track).Should().Be(MusicContentRatingSource.FileTag);
    }

    [Fact]
    public async Task Unlocking_hands_the_rating_back_to_the_tag_and_provider()
    {
        var track = GivenTrack("Clean", null, nameof(Track.ContentRating));

        await Save(track, "Clean");

        track.IsLocked(nameof(Track.ContentRating)).Should().BeFalse();
    }

    [Fact]
    public async Task Setting_an_album_rates_and_locks_every_track()
    {
        var albumId = Guid.NewGuid();
        var tracks = new List<Track>
        {
            new() { Id = Guid.NewGuid(), Title = "White America", ContentRating = "Clean", ContentRatingProvider = "deezer_content_ratings" },
            new() { Id = Guid.NewGuid(), Title = "Business" },
        };
        _repository.GetAlbumTracksForUpdateAsync(albumId).Returns(tracks);

        (await _manager.SetAlbumContentRatingAsync(albumId, "Explicit")).Should().BeTrue();

        tracks.Should().OnlyContain(t => t.ContentRating == "Explicit" && t.ContentRatingProvider == null && t.IsLocked(nameof(Track.ContentRating)));
        await _repository.Received(1).SaveMusicChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_album_with_no_tracks_is_not_found()
    {
        _repository.GetAlbumTracksForUpdateAsync(Arg.Any<Guid>()).Returns(new List<Track>());

        (await _manager.SetAlbumContentRatingAsync(Guid.NewGuid(), "Clean")).Should().BeFalse();
    }

    [Theory]
    [InlineData("explicit", true, "Explicit")]
    [InlineData(" Clean ", true, "Clean")]
    [InlineData("", true, null)]
    [InlineData(null, true, null)]
    [InlineData("PG", false, null)]
    public void Only_explicit_clean_or_none_are_music_ratings(string? input, bool valid, string? expected)
    {
        MusicContentRating.TryNormalize(input, out var rating).Should().Be(valid);
        rating.Should().Be(expected);
    }

    [Fact]
    public void A_rating_from_a_provider_says_so()
    {
        var track = new Track { Title = "x", ContentRating = "Clean", ContentRatingProvider = "deezer_content_ratings" };

        MusicContentRating.SourceOf(track).Should().Be(MusicContentRatingSource.Provider);
    }
}
