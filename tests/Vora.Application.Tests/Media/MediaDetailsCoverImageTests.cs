using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Artwork;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Subtitles;
using Vora.Application.Tasks;
using Vora.Application.Thumbnails;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Media;

public class MediaDetailsCoverImageTests
{
    private readonly IMediaRepository _repository = Substitute.For<IMediaRepository>();

    private MediaManager Manager() => new(
        _repository,
        Substitute.For<IUserMediaStateRepository>(),
        Substitute.For<IClientNotifier>(),
        Substitute.For<ITaskQueueManager>(),
        Substitute.For<ISystemSettingsRepository>(),
        [],
        Options.Create(new StoragePathsOptions()),
        Substitute.For<IVideoThumbnailStorageService>(),
        Substitute.For<IArtworkThumbnailService>(),
        Substitute.For<ISubtitlePreExtractionManager>(),
        NullLogger<MediaManager>.Instance);

    private static MediaDetailsVM Episode(params MediaDetailsPartVideoTrackVM[] videoTracks) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Shaketopia",
        MediaParts = [new MediaDetailsPartVM { Id = Guid.NewGuid(), Resolution = "720p", VideoTracks = videoTracks.ToList() }]
    };

    private void Stored(MediaDetailsVM details) =>
        _repository.GetProjectedAsync(details.Id, Arg.Any<Expression<Func<MediaItem, MediaDetailsVM>>>(),
                Arg.Any<bool>(), Arg.Any<List<Guid>?>(), Arg.Any<List<string>?>(), Arg.Any<List<string>?>(), Arg.Any<bool>())
            .Returns(details);

    [Fact]
    public async Task A_cover_image_is_not_offered_as_a_video_track()
    {
        var details = Episode(
            new MediaDetailsPartVideoTrackVM { Id = Guid.NewGuid(), Codec = "h264", IsDefault = true },
            new MediaDetailsPartVideoTrackVM { Id = Guid.NewGuid(), Codec = "mjpeg" });
        Stored(details);

        var shown = await Manager().GetMediaItemAsync(details.Id);

        shown!.MediaParts.Single().VideoTracks.Should().ContainSingle().Which.Codec.Should().Be("h264");
    }

    [Fact]
    public async Task A_profile_sees_the_same_tracks()
    {
        var details = Episode(
            new MediaDetailsPartVideoTrackVM { Id = Guid.NewGuid(), Codec = "png" },
            new MediaDetailsPartVideoTrackVM { Id = Guid.NewGuid(), Codec = "hevc", IsDefault = true });
        Stored(details);

        var shown = await Manager().GetMediaItemAsync(details.Id, Guid.NewGuid(), hasAllAccess: true, [], [], [], blockUnrated: false);

        shown!.MediaParts.Single().VideoTracks.Should().ContainSingle().Which.Codec.Should().Be("hevc");
    }

    [Fact]
    public async Task A_missing_item_stays_missing()
    {
        (await Manager().GetMediaItemAsync(Guid.NewGuid())).Should().BeNull();
    }
}
