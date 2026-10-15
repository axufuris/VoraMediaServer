using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Vora.Application.Metadata;
using Vora.Application.Settings;
using Vora.Application.Thumbnails;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Metadata;

public class VideoFrameArtworkServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-frame-artwork-" + Guid.NewGuid().ToString("N"));
    private readonly IVideoThumbnailGeneratorService _generator = Substitute.For<IVideoThumbnailGeneratorService>();
    private readonly VideoFrameArtworkService _service;
    private readonly Queue<(int Width, int Height, Rgb24 Colour)> _frames = new();

    private static readonly Rgb24 Lit = new(220, 180, 120);
    private static readonly Rgb24 Dark = new(4, 4, 4);

    public VideoFrameArtworkServiceTests()
    {
        Directory.CreateDirectory(_root);
        _service = new VideoFrameArtworkService(_generator, Options.Create(new StoragePathsOptions { CustomArtwork = Path.Combine(_root, "artwork") }), NullLogger<VideoFrameArtworkService>.Instance);
        _generator.ProbeDurationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(TimeSpan.FromSeconds(90));
        _generator.ExtractFrameAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (_frames.Count == 0) return false;
                var (width, height, colour) = _frames.Dequeue();
                using var image = new Image<Rgb24>(width, height, colour);
                image.SaveAsJpeg((string)call[2]);
                return true;
            });
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private Movie Video(TimeSpan? duration = null, bool fileExists = true)
    {
        var file = Path.Combine(_root, "Family", "IMG_0001.mov");
        if (fileExists)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file) ?? _root);
            File.WriteAllText(file, string.Empty);
        }
        var video = new Movie { Id = Guid.NewGuid(), Title = "IMG_0001" };
        video.MediaParts.Add(new MediaPart { Id = Guid.NewGuid(), FilePath = file, Duration = duration, MediaItemId = video.Id });
        return video;
    }

    private string Saved(MediaArtwork artwork) => Path.Combine(_root, "artwork", artwork.Url["/api/artwork/custom/".Length..]);

    [Fact]
    public async Task A_frame_becomes_a_poster_and_a_backdrop_in_their_own_shapes()
    {
        _frames.Enqueue((1920, 1080, Lit));

        var artwork = await _service.GetFrameArtworkAsync(Video(), needsPoster: true, needsBackdrop: true, TestContext.Current.CancellationToken);

        artwork.Select(a => a.Kind).Should().BeEquivalentTo([ArtworkKind.Poster, ArtworkKind.Backdrop]);
        artwork.Should().OnlyContain(a => a.Url.StartsWith("/api/artwork/custom/") && a.ProviderId == VideoFrameArtworkService.ProviderId);
        using var poster = await Image.LoadAsync<Rgb24>(Saved(artwork.Single(a => a.Kind == ArtworkKind.Poster)), TestContext.Current.CancellationToken);
        using var backdrop = await Image.LoadAsync<Rgb24>(Saved(artwork.Single(a => a.Kind == ArtworkKind.Backdrop)), TestContext.Current.CancellationToken);
        poster.Size.Should().Be(VideoFrameArtworkService.PosterSize);
        backdrop.Size.Should().Be(VideoFrameArtworkService.BackdropSize);
        poster[poster.Width / 2, poster.Height / 2].R.Should().BeInRange(205, 235);
    }

    [Fact]
    public async Task A_portrait_phone_video_is_fitted_into_the_poster_over_a_darkened_copy()
    {
        _frames.Enqueue((1080, 1920, Lit));

        var artwork = await _service.GetFrameArtworkAsync(Video(), needsPoster: true, needsBackdrop: false, TestContext.Current.CancellationToken);

        using var poster = await Image.LoadAsync<Rgb24>(Saved(artwork.Single()), TestContext.Current.CancellationToken);
        poster[poster.Width / 2, poster.Height / 2].R.Should().BeGreaterThan(200);
        poster[5, poster.Height / 2].R.Should().BeLessThan(160);
    }

    [Fact]
    public async Task A_dark_frame_is_passed_over_for_a_lit_one()
    {
        _frames.Enqueue((1920, 1080, Dark));
        _frames.Enqueue((1920, 1080, Lit));

        var artwork = await _service.GetFrameArtworkAsync(Video(), needsPoster: true, needsBackdrop: false, TestContext.Current.CancellationToken);

        using var poster = await Image.LoadAsync<Rgb24>(Saved(artwork.Single()), TestContext.Current.CancellationToken);
        poster[poster.Width / 2, poster.Height / 2].R.Should().BeGreaterThan(200);
        await _generator.Received(1).ExtractFrameAsync(Arg.Any<string>(), TimeSpan.FromSeconds(30), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _generator.Received(1).ExtractFrameAsync(Arg.Any<string>(), TimeSpan.FromSeconds(45), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _generator.DidNotReceive().ExtractFrameAsync(Arg.Any<string>(), TimeSpan.FromSeconds(60), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_video_dark_all_the_way_through_still_gets_a_poster()
    {
        _frames.Enqueue((1920, 1080, Dark));
        _frames.Enqueue((1920, 1080, Dark));
        _frames.Enqueue((1920, 1080, Dark));

        var artwork = await _service.GetFrameArtworkAsync(Video(), needsPoster: true, needsBackdrop: false, TestContext.Current.CancellationToken);

        artwork.Should().ContainSingle();
    }

    [Fact]
    public async Task The_duration_from_analysis_is_used_when_it_is_known()
    {
        _frames.Enqueue((1920, 1080, Lit));

        await _service.GetFrameArtworkAsync(Video(TimeSpan.FromSeconds(12)), needsPoster: true, needsBackdrop: false, TestContext.Current.CancellationToken);

        await _generator.DidNotReceive().ProbeDurationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _generator.Received(1).ExtractFrameAsync(Arg.Any<string>(), TimeSpan.FromSeconds(4), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_frame_is_taken_once_and_kept()
    {
        var video = Video();
        _frames.Enqueue((1920, 1080, Lit));

        var first = await _service.GetFrameArtworkAsync(video, needsPoster: true, needsBackdrop: true, TestContext.Current.CancellationToken);
        var again = await _service.GetFrameArtworkAsync(video, needsPoster: true, needsBackdrop: true, TestContext.Current.CancellationToken);

        again.Select(a => a.Url).Should().Equal(first.Select(a => a.Url));
        await _generator.Received(1).ExtractFrameAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nothing_is_made_without_a_readable_file_or_a_frame()
    {
        (await _service.GetFrameArtworkAsync(Video(fileExists: false), needsPoster: true, needsBackdrop: true, TestContext.Current.CancellationToken)).Should().BeEmpty();
        await _generator.DidNotReceive().ExtractFrameAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        (await _service.GetFrameArtworkAsync(Video(), needsPoster: true, needsBackdrop: true, TestContext.Current.CancellationToken)).Should().BeEmpty();
    }
}
