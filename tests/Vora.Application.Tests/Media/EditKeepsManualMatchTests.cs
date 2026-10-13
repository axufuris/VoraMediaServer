using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Artwork;
using Vora.Application.Media;
using Vora.Application.Media.Requests;
using Vora.Application.Metadata;
using Vora.Application.Settings;
using Vora.Application.Subtitles;
using Vora.Application.Tasks;
using Vora.Application.Thumbnails;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Media;

public class EditKeepsManualMatchTests
{
    private readonly IMediaRepository _repository = Substitute.For<IMediaRepository>();

    private MediaManager Manager() => new(
        _repository,
        Substitute.For<IUserMediaStateRepository>(),
        Substitute.For<IClientNotifier>(),
        Substitute.For<ITaskQueueManager>(),
        Substitute.For<ISystemSettingsRepository>(),
        Array.Empty<ILocalMediaScannerProvider>(),
        Options.Create(new StoragePathsOptions()),
        Substitute.For<IVideoThumbnailStorageService>(),
        Substitute.For<IArtworkThumbnailService>(),
        Substitute.For<ISubtitlePreExtractionManager>(),
        NullLogger<MediaManager>.Instance);

    private Movie Held(params string[] locked)
    {
        var movie = new Movie { Title = "Alien", LockedFields = locked.ToList() };
        _repository.GetForBasicUpdateAsync(movie.Id).Returns(movie);
        return movie;
    }

    [Fact]
    public async Task Saving_edited_details_without_the_match_mark_does_not_undo_a_match_made_by_hand()
    {
        var movie = Held(MediaMatchManager.MatchLock, nameof(MediaItem.Overview));

        await Manager().UpdateMediaMetadataAsync(movie.Id, new UpdateMediaRequest { Title = "Alien", LockedFields = [nameof(MediaItem.Title)] });

        movie.LockedFields.Should().BeEquivalentTo(nameof(MediaItem.Title), MediaMatchManager.MatchLock);
    }

    [Fact]
    public async Task Saving_edited_details_never_adds_the_mark_to_a_title_that_was_matched_automatically()
    {
        var movie = Held(nameof(MediaItem.Overview));

        await Manager().UpdateMediaMetadataAsync(movie.Id, new UpdateMediaRequest { Title = "Alien", LockedFields = [nameof(MediaItem.Title)] });

        movie.LockedFields.Should().Equal(nameof(MediaItem.Title));
    }

    [Fact]
    public async Task The_mark_cannot_be_added_by_editing_details_only_by_fixing_the_match()
    {
        var movie = Held();

        await Manager().UpdateMediaMetadataAsync(movie.Id, new UpdateMediaRequest { Title = "Alien", LockedFields = [nameof(MediaItem.Title), "match"] });

        movie.LockedFields.Should().Equal(nameof(MediaItem.Title));
    }
}
