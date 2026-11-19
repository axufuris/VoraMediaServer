using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Vora.Application.Artwork;
using Vora.Application.Media;
using Vora.Application.Posters;
using Vora.Application.Posters.Dtos;
using Vora.Application.Posters.Providers;
using Vora.Application.Settings;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Posters;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Posters;

// The Multi-Audio poster tag (issue #307): the badge file, when it is drawn, how
// the poster's cache name reflects it, and which titles the overlay step flags.
public sealed class MultiAudioOverlayTests : IDisposable
{
    private const int Width = 600;
    private const int Height = 900;
    private const string Layout = """[{"type":"multi_audio","xPct":0.05,"yPct":0.05,"widthPct":0.3,"heightPct":0.1}]""";

    private static readonly string BasePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Overlays");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-multi-audio-" + Guid.NewGuid().ToString("N"));
    private readonly IMediaRepository _media = Substitute.For<IMediaRepository>();
    private readonly IOverlayTemplateRepository _templates = Substitute.For<IOverlayTemplateRepository>();
    private readonly IOverlayProvider _provider = Substitute.For<IOverlayProvider>();
    private readonly PosterOverlayManager _manager;
    private OverlayMediaDto? _drawn;

    public MultiAudioOverlayTests()
    {
        Directory.CreateDirectory(_root);
        _provider.GenerateOverlayAsync(Arg.Do<OverlayMediaDto>(d => _drawn = d), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => $"/api/artwork/custom/{call.Arg<OverlayMediaDto>().Id}_overlay_x.jpg");

        _manager = new PosterOverlayManager(
            _media,
            _templates,
            [_provider],
            Microsoft.Extensions.Options.Options.Create(new StoragePathsOptions { CustomArtwork = Path.Combine(_root, "custom"), OriginalArtworkCache = Path.Combine(_root, "original") }),
            Substitute.For<IHttpClientFactory>(),
            new NullTaskProgressReporter(),
            Substitute.For<IArtworkThumbnailService>(),
            Substitute.For<ILocalArtworkLocator>(),
            Substitute.For<IServiceScopeFactory>(),
            NullLogger<PosterOverlayManager>.Instance);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    // --- the badge -----------------------------------------------------------

    [Fact]
    public void A_multi_audio_title_gets_the_badge_file_and_the_badge_ships_with_the_server()
    {
        var path = BadgeResolver.DetermineBadgePath(new OverlayMediaDto { HasMultipleAudio = true }, "multi_audio", BasePath);

        path.Should().Be(Path.Combine(BasePath, "MultiAudio", "MultiAudio.png"));
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void A_title_with_one_audio_language_gets_no_badge()
    {
        BadgeResolver.DetermineBadgePath(new OverlayMediaDto { HasMultipleAudio = false }, "multi_audio", BasePath).Should().BeNull();
    }

    // --- drawing -------------------------------------------------------------

    private async Task<(string Url, Image<Rgba32> Poster)> RenderAsync(OverlayMediaDto item)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = Path.Combine(_root, "poster.png");
        if (!File.Exists(source))
        {
            using var white = new Image<Rgba32>(Width, Height, new Rgba32(255, 255, 255, 255));
            await white.SaveAsPngAsync(source, cancellationToken);
        }

        var url = await new LocalImageSharpOverlayProvider(NullLogger<LocalImageSharpOverlayProvider>.Instance)
            .GenerateOverlayAsync(item, source, Layout, _root, cancellationToken);

        return (url, await Image.LoadAsync<Rgba32>(Path.Combine(_root, url["/api/artwork/custom/".Length..]), cancellationToken));
    }

    [Fact]
    public async Task The_tag_is_drawn_on_a_darkened_box_when_the_title_is_multi_audio()
    {
        var (_, poster) = await RenderAsync(new OverlayMediaDto { Id = Guid.NewGuid(), MediaType = "Movie", HasMultipleAudio = true });
        using var drawn = poster;

        poster[(int)(Width * 0.05) + 4, (int)(Height * 0.05) + 4].R.Should().BeLessThan(120);
        poster[Width / 2, Height / 2].R.Should().BeGreaterThan(230);
    }

    [Fact]
    public async Task Nothing_is_drawn_when_the_title_has_one_audio_language()
    {
        var (_, poster) = await RenderAsync(new OverlayMediaDto { Id = Guid.NewGuid(), MediaType = "Movie", HasMultipleAudio = false });
        using var drawn = poster;

        poster[(int)(Width * 0.05) + 4, (int)(Height * 0.05) + 4].R.Should().BeGreaterThan(230);
    }

    [Fact]
    public async Task A_title_that_gains_a_language_is_drawn_under_a_new_file_name_while_every_other_keeps_its_own()
    {
        var id = Guid.NewGuid();
        var (single, poster1) = await RenderAsync(new OverlayMediaDto { Id = id, MediaType = "Movie" });
        poster1.Dispose();
        var (singleAgain, poster2) = await RenderAsync(new OverlayMediaDto { Id = id, MediaType = "Movie", HasMultipleAudio = false });
        poster2.Dispose();
        var (multi, poster3) = await RenderAsync(new OverlayMediaDto { Id = id, MediaType = "Movie", HasMultipleAudio = true });
        poster3.Dispose();

        singleAgain.Should().Be(single);
        multi.Should().NotBe(single);
    }

    [Fact]
    public void A_template_saved_with_the_tag_reads_back_as_an_element_of_that_type()
    {
        JsonSerializer.Deserialize<List<OverlayElementDto>>(Layout, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!
            .Should().ContainSingle().Which.Type.Should().Be("multi_audio");
    }

    // --- which titles the overlay step flags --------------------------------

    private async Task<OverlayMediaDto> DrawnAsync(MediaItem item, string mediaType, params MediaAudioTrack[][] audioByPart)
    {
        var poster = Path.Combine(_root, $"{item.Id:N}.jpg");
        File.WriteAllBytes(poster, [1]);
        item.PosterUrl = poster;
        item.OriginalPosterUrl = poster;
        foreach (var tracks in audioByPart)
        {
            item.MediaParts.Add(new MediaPart { FilePath = $"/media/{Guid.NewGuid():N}.mkv", AudioTracks = tracks.ToList() });
        }

        _media.GetForPosterOverlayAsync(item.Id).Returns(item);
        _templates.GetTemplatesForLibraryAsync(Arg.Any<Guid>())
            .Returns(new List<OverlayTemplate> { new() { TargetMediaType = mediaType, ConfigurationJson = Layout } });

        _drawn = null;
        await _manager.GenerateOverlaysForMediaAsync(item.Id, TestContext.Current.CancellationToken);

        _drawn.Should().NotBeNull();
        return _drawn!;
    }

    private static MediaAudioTrack Track(string language) => new() { Language = language, Codec = "eac3", Channels = 6 };

    [Fact]
    public async Task A_movie_with_english_and_japanese_audio_is_flagged()
    {
        var dto = await DrawnAsync(new Movie { Title = "Film" }, "Movie", [Track("eng"), Track("jpn")]);

        dto.HasMultipleAudio.Should().BeTrue();
    }

    [Fact]
    public async Task A_movie_whose_versions_each_carry_a_different_language_is_flagged()
    {
        var dto = await DrawnAsync(new Movie { Title = "Film" }, "Movie", [Track("eng")], [Track("fra")]);

        dto.HasMultipleAudio.Should().BeTrue();
    }

    [Fact]
    public async Task A_movie_whose_versions_all_carry_the_same_language_is_not_flagged()
    {
        var dto = await DrawnAsync(new Movie { Title = "Film" }, "Movie", [Track("eng")], [Track("eng")]);

        dto.HasMultipleAudio.Should().BeFalse();
    }

    [Fact]
    public async Task An_episode_with_two_languages_is_flagged()
    {
        var dto = await DrawnAsync(new Episode { Title = "Pilot", EpisodeNumber = 1 }, "Episode", [Track("eng"), Track("spa")]);

        dto.HasMultipleAudio.Should().BeTrue();
    }

    [Fact]
    public async Task A_show_and_a_season_have_no_files_so_they_are_not_flagged()
    {
        (await DrawnAsync(new TvShow { Title = "Show" }, "TvShow")).HasMultipleAudio.Should().BeFalse();
        (await DrawnAsync(new Season { Title = "Season 1", SeasonNumber = 1 }, "Season")).HasMultipleAudio.Should().BeFalse();
    }
}
