using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Libraries;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Thumbnails;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Thumbnails;

public class VideoThumbnailManagerTests
{
    // NOTE: production code reads projected metadata via private anonymous types
    // (e.g. `GetProjectedAsync(id, m => new { LibraryType = m.Library.Type, ... })`).
    // NSubstitute cannot bridge generic calls whose T is a different anonymous type
    // than the one captured at Returns() time, so the meta-projection paths cannot
    // be meaningfully stubbed without refactoring production to named DTOs.
    // The tests here cover only the static helpers and the type-string routing.

    private readonly IMediaRepository _media;
    private readonly ILibraryRepository _library;
    private readonly ISystemSettingsRepository _settings;
    private readonly IVideoThumbnailStorageService _storage;
    private readonly IVideoThumbnailGeneratorService _generator;
    private readonly IClientNotifier _notifier;
    private readonly VideoThumbnailManager _manager;

    public VideoThumbnailManagerTests()
    {
        _media = Substitute.For<IMediaRepository>();
        _library = Substitute.For<ILibraryRepository>();
        _settings = Substitute.For<ISystemSettingsRepository>();
        _storage = Substitute.For<IVideoThumbnailStorageService>();
        _generator = Substitute.For<IVideoThumbnailGeneratorService>();
        _notifier = Substitute.For<IClientNotifier>();

        _settings.GetSettingsAsync().Returns(new ServerSetting
        {
            VideoThumbnailIntervalSeconds = 10,
            VideoThumbnailWidth = 320,
            VideoThumbnailHeight = 180,
            VideoThumbnailJpegQuality = 80,
            VideoThumbnailSpriteColumns = 10
        });

        _manager = new VideoThumbnailManager(
            _media, _library, _settings, _storage, _generator, _notifier,
            Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            NullLogger<VideoThumbnailManager>.Instance);
    }

    [Theory]
    [InlineData(LibraryType.Movie, true)]
    [InlineData(LibraryType.TvShow, true)]
    [InlineData(LibraryType.HomeVideo, true)]
    [InlineData(LibraryType.Music, false)]
    [InlineData(LibraryType.LiveTv, false)]
    public void IsVideoBearingLibrary_classifies_library_types(LibraryType type, bool expected)
    {
        VideoThumbnailManager.IsVideoBearingLibrary(type).Should().Be(expected);
    }

    [Fact]
    public void ComputeSpriteVersion_is_deterministic_for_same_inputs()
    {
        var v1 = VideoThumbnailManager.ComputeSpriteVersion(10, 320, 180, 80, 10);
        var v2 = VideoThumbnailManager.ComputeSpriteVersion(10, 320, 180, 80, 10);
        v1.Should().Be(v2);
        v1.Length.Should().Be(12);
    }

    [Fact]
    public void ComputeSpriteVersion_changes_when_any_input_changes()
    {
        var baseVersion = VideoThumbnailManager.ComputeSpriteVersion(10, 320, 180, 80, 10);

        VideoThumbnailManager.ComputeSpriteVersion(20, 320, 180, 80, 10).Should().NotBe(baseVersion);
        VideoThumbnailManager.ComputeSpriteVersion(10, 640, 180, 80, 10).Should().NotBe(baseVersion);
        VideoThumbnailManager.ComputeSpriteVersion(10, 320, 360, 80, 10).Should().NotBe(baseVersion);
        VideoThumbnailManager.ComputeSpriteVersion(10, 320, 180, 90, 10).Should().NotBe(baseVersion);
        VideoThumbnailManager.ComputeSpriteVersion(10, 320, 180, 80, 20).Should().NotBe(baseVersion);
    }

    [Fact]
    public async Task TriggerMediaItemThumbnailGenerationAsync_skips_non_video_types()
    {
        var id = Guid.NewGuid();
        _media.GetProjectedAsync(id, Arg.Any<Expression<Func<MediaItem, string>>>()).Returns("Artist");

        await _manager.TriggerMediaItemThumbnailGenerationAsync(id);

        await _media.DidNotReceive().GetMediaFilePathsAsync(Arg.Any<Guid>());
        await _generator.DidNotReceiveWithAnyArgs().GenerateAsync(default!, default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TriggerMediaItemThumbnailGenerationAsync_routes_tv_show_to_episode_ids()
    {
        var tvId = Guid.NewGuid();
        var ep1 = Guid.NewGuid();
        var ep2 = Guid.NewGuid();

        _media.GetProjectedAsync(tvId, Arg.Any<Expression<Func<MediaItem, string>>>()).Returns("TvShow");
        _media.GetEpisodeIdsForShowAsync(tvId).Returns(new List<Guid> { ep1, ep2 });

        await _manager.TriggerMediaItemThumbnailGenerationAsync(tvId);

        await _media.Received(1).GetEpisodeIdsForShowAsync(tvId);
    }

    [Fact]
    public async Task TriggerMediaItemThumbnailGenerationAsync_routes_season_to_episode_ids()
    {
        var seasonId = Guid.NewGuid();
        var ep1 = Guid.NewGuid();

        _media.GetProjectedAsync(seasonId, Arg.Any<Expression<Func<MediaItem, string>>>()).Returns("Season");
        _media.GetEpisodeIdsForSeasonAsync(seasonId).Returns(new List<Guid> { ep1 });

        await _manager.TriggerMediaItemThumbnailGenerationAsync(seasonId);

        await _media.Received(1).GetEpisodeIdsForSeasonAsync(seasonId);
        await _media.DidNotReceive().GetEpisodeIdsForShowAsync(Arg.Any<Guid>());
    }
}
