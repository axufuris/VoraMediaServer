using Vora.Application.Media;
using Vora.Application.Metadata;
using Vora.Application.Streaming;

namespace Vora.Application.Tests.Media;

public class ExtraThumbnailServiceTests
{
    private readonly IStreamRepository _streams = Substitute.For<IStreamRepository>();
    private readonly IVideoFrameArtworkService _frames = Substitute.For<IVideoFrameArtworkService>();
    private readonly ExtraThumbnailService _service;
    private readonly Guid _extraId = Guid.NewGuid();
    private readonly ExtraThumbnailPartDto _part = new() { Id = Guid.NewGuid(), FilePath = "/media/Movie/Trailers/Teaser.mkv", Duration = TimeSpan.FromSeconds(90) };

    public ExtraThumbnailServiceTests()
    {
        _service = new ExtraThumbnailService(_streams, _frames);
    }

    private void Source(ExtraThumbnailPartDto? part, string? fallbackUrl) =>
        _streams.GetExtraThumbnailSourceAsync(_extraId).Returns(new ExtraThumbnailSourceDto { ExtraId = _extraId, Part = part, FallbackUrl = fallbackUrl });

    private void Frame(string? path) =>
        _frames.GetExtraThumbnailAsync(_extraId, _part.Id, _part.FilePath, _part.Duration, Arg.Any<CancellationToken>()).Returns(path);

    [Fact]
    public async Task An_extra_that_does_not_exist_has_no_thumbnail()
    {
        _streams.GetExtraThumbnailSourceAsync(_extraId).Returns((ExtraThumbnailSourceDto?)null);

        (await _service.GetAsync(_extraId, TestContext.Current.CancellationToken)).Should().BeNull();
        await _frames.DidNotReceive().GetExtraThumbnailAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_frame_from_the_extras_own_file_comes_first()
    {
        Source(_part, "https://image.example/backdrop.jpg");
        Frame("/artwork/frame.jpg");

        var thumbnail = await _service.GetAsync(_extraId, TestContext.Current.CancellationToken);

        thumbnail.Should().Be(new ExtraThumbnail("/artwork/frame.jpg", "https://image.example/backdrop.jpg"));
    }

    [Fact]
    public async Task When_no_frame_can_be_taken_the_titles_backdrop_stands_in()
    {
        Source(_part, "https://image.example/backdrop.jpg");
        Frame(null);

        var thumbnail = await _service.GetAsync(_extraId, TestContext.Current.CancellationToken);

        thumbnail.Should().Be(new ExtraThumbnail(null, "https://image.example/backdrop.jpg"));
    }

    [Fact]
    public async Task An_extra_without_a_file_uses_the_backdrop_without_asking_for_a_frame()
    {
        Source(null, "/api/artwork/custom/backdrop.jpg");

        var thumbnail = await _service.GetAsync(_extraId, TestContext.Current.CancellationToken);

        thumbnail.Should().Be(new ExtraThumbnail(null, "/api/artwork/custom/backdrop.jpg"));
        await _frames.DidNotReceive().GetExtraThumbnailAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task With_no_frame_and_no_backdrop_there_is_nothing_to_show(string? fallbackUrl)
    {
        Source(_part, fallbackUrl);
        Frame(null);

        (await _service.GetAsync(_extraId, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task A_blank_backdrop_is_not_offered_beside_a_frame()
    {
        Source(_part, " ");
        Frame("/artwork/frame.jpg");

        var thumbnail = await _service.GetAsync(_extraId, TestContext.Current.CancellationToken);

        thumbnail.Should().Be(new ExtraThumbnail("/artwork/frame.jpg", null));
    }

    [Fact]
    public void An_extras_thumbnail_address_is_built_from_its_id()
    {
        var extra = new MediaExtraVM { Id = _extraId };

        extra.ThumbnailUrl.Should().Be($"/api/artwork/extra/{_extraId}");
        System.Text.Json.JsonSerializer.Serialize(extra, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
            .Should().Contain($"\"thumbnailUrl\":\"/api/artwork/extra/{_extraId}\"");
    }
}
