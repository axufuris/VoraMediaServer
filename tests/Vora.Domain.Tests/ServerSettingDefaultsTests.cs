using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;

namespace Vora.Domain.Tests;

public class ServerSettingDefaultsTests
{
    private readonly ServerSetting _settings = new();

    [Fact]
    public void Resolves_tvdb_ids_for_movies_and_shows()
    {
        _settings.ResolveMovieTvdbIds.Should().BeTrue();
    }

    [Fact]
    public void Prefers_direct_stream()
    {
        _settings.StreamingProfile.Should().Be(StreamingProfile.DirectStreamPreference);
    }

    [Fact]
    public void Never_generates_preview_thumbnails_on_its_own()
    {
        _settings.VideoThumbnailGeneration.Should().Be(DetectionTrigger.Never);
    }

    [Fact]
    public void Keeps_what_the_old_seed_row_set()
    {
        _settings.EnableNightlyScan.Should().BeFalse();
        _settings.RunDetections.Should().Be(DetectionTrigger.Never);
    }
}
