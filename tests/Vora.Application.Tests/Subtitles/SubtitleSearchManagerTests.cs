using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Subtitles;
using Vora.Application.Subtitles.ViewModels;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Settings;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Subtitles;

// Find Subtitles is provider-backed and swappable, so the manager's job is
// picking a usable provider, turning an item into a query the provider can
// answer, and landing the download in the server's own store as a selectable
// track. The clients hide the whole feature on availability, so "is anything
// configured" has to be honest.
public class SubtitleSearchManagerTests
{
    private static readonly Guid MediaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PartId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IMediaRepository _media = Substitute.For<IMediaRepository>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly ISubtitleExtractionService _extractor = Substitute.For<ISubtitleExtractionService>();

    private readonly string _store = Path.Combine(Path.GetTempPath(), "vora-sub-store-" + Guid.NewGuid().ToString("N"));

    private static ISubtitleSearchProvider Provider(string id, bool configured, IReadOnlyList<SubtitleSearchResultDto>? results = null)
    {
        var provider = Substitute.For<ISubtitleSearchProvider>();
        provider.Id.Returns(id);
        provider.Name.Returns(id);
        provider.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(configured);
        provider.SearchAsync(Arg.Any<SubtitleSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(results ?? Array.Empty<SubtitleSearchResultDto>());
        return provider;
    }

    private SubtitleSearchManager NewManager(params ISubtitleSearchProvider[] providers) => new(
        providers,
        _media,
        _settings,
        _extractor,
        Options.Create(new StoragePathsOptions { Subtitles = _store }),
        NullLogger<SubtitleSearchManager>.Instance);

    private void Arrange(string? activeProviderId = null, SubtitleSearchFactsDto? facts = null)
    {
        _settings.GetSettingsAsync().Returns(new ServerSetting { SubtitleSearchProviderId = activeProviderId ?? string.Empty });
        _media.GetSubtitleSearchFactsAsync(MediaId).Returns(facts ?? new SubtitleSearchFactsDto { Title = "The Odyssey", Year = 2026, ImdbId = "tt1368337" });
        _media.GetPrimaryMediaPartIdAsync(MediaId).Returns(PartId);
        _extractor.ConvertToWebVttAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var destination = call.ArgAt<string>(1);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllText(destination, "WEBVTT");
                return true;
            });
    }

    [Fact]
    public async Task No_installed_provider_means_the_feature_is_unavailable()
    {
        Arrange();

        (await NewManager().IsAvailableAsync()).Should().BeFalse();
    }

    // An installed but unconfigured provider must not advertise the feature: the
    // clients would show Find Subtitles and every search would come back empty.
    [Fact]
    public async Task An_unconfigured_provider_means_the_feature_is_unavailable()
    {
        Arrange();

        (await NewManager(Provider("opensubtitles_search", configured: false)).IsAvailableAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task A_configured_provider_makes_the_feature_available()
    {
        Arrange();

        (await NewManager(Provider("opensubtitles_search", configured: true)).IsAvailableAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task The_admin_selected_provider_is_used()
    {
        Arrange(activeProviderId: "second");
        var first = Provider("first", configured: true);
        var second = Provider("second", configured: true);

        await NewManager(first, second).SearchAsync(MediaId, ["en"]);

        await second.Received(1).SearchAsync(Arg.Any<SubtitleSearchQuery>(), Arg.Any<CancellationToken>());
        await first.DidNotReceive().SearchAsync(Arg.Any<SubtitleSearchQuery>(), Arg.Any<CancellationToken>());
    }

    // Picking a provider and never entering its key would otherwise hide a second
    // provider that is ready to work, and the feature would look broken.
    [Fact]
    public async Task An_unusable_selection_falls_back_to_a_provider_that_works()
    {
        Arrange(activeProviderId: "unconfigured");
        var unconfigured = Provider("unconfigured", configured: false);
        var working = Provider("working", configured: true);

        await NewManager(unconfigured, working).SearchAsync(MediaId, ["en"]);

        await working.Received(1).SearchAsync(Arg.Any<SubtitleSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_provider_that_throws_while_reporting_readiness_is_skipped()
    {
        Arrange();
        var broken = Provider("broken", configured: false);
        broken.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns<bool>(_ => throw new HttpRequestException("down"));
        var working = Provider("working", configured: true);

        (await NewManager(broken, working).IsAvailableAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task Searching_with_no_provider_returns_nothing_rather_than_throwing()
    {
        Arrange();

        (await NewManager().SearchAsync(MediaId, ["en"])).Should().BeEmpty();
    }

    // A provider matches an episode on the SERIES plus season and episode
    // numbers. Sending the episode's own title finds nothing.
    [Fact]
    public async Task An_episode_is_searched_by_its_series_and_numbering()
    {
        Arrange(facts: new SubtitleSearchFactsDto
        {
            Title = "Pilot",
            SeriesTitle = "How I Met Your Father",
            Year = 2022,
            ImdbId = "tt14500874",
            SeasonNumber = 1,
            EpisodeNumber = 1,
        });
        var provider = Provider("opensubtitles_search", configured: true);

        await NewManager(provider).SearchAsync(MediaId, ["en"]);

        await provider.Received(1).SearchAsync(
            Arg.Is<SubtitleSearchQuery>(q =>
                q.Title == "How I Met Your Father" && q.Season == 1 && q.Episode == 1 && q.IsEpisode),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_movie_is_searched_by_its_own_title_with_no_numbering()
    {
        Arrange();
        var provider = Provider("opensubtitles_search", configured: true);

        await NewManager(provider).SearchAsync(MediaId, ["en", "es"]);

        await provider.Received(1).SearchAsync(
            Arg.Is<SubtitleSearchQuery>(q =>
                q.Title == "The Odyssey" && q.Year == 2026 && q.ImdbId == "tt1368337" && !q.IsEpisode
                && q.Languages.Count == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_download_becomes_a_selectable_track_in_the_server_store()
    {
        Arrange();
        var provider = Provider("opensubtitles_search", configured: true);
        provider.DownloadAsync("9001", Arg.Any<CancellationToken>())
            .Returns(new SubtitleDownloadDto { Content = [1, 2, 3], Format = "srt", Language = "en" });

        var result = await NewManager(provider).DownloadAndAttachAsync(MediaId, "9001", "en");

        result.Should().NotBeNull();
        result!.MediaPartId.Should().Be(PartId);
        result.Codec.Should().Be("webvtt");

        await _media.Received(1).AddSubtitleTrackAsync(Arg.Is<MediaSubtitleTrack>(t =>
            t.IsDownloaded && t.ExternalFilePath != null && t.MediaPartId == PartId));
    }

    // The library is read-only. A downloaded file must land in the server's own
    // store, never beside the video.
    [Fact]
    public async Task A_downloaded_file_is_written_under_the_server_subtitle_store()
    {
        Arrange();
        var provider = Provider("opensubtitles_search", configured: true);
        provider.DownloadAsync("9001", Arg.Any<CancellationToken>())
            .Returns(new SubtitleDownloadDto { Content = [1, 2, 3], Format = "srt" });

        MediaSubtitleTrack? saved = null;
        await _media.AddSubtitleTrackAsync(Arg.Do<MediaSubtitleTrack>(t => saved = t));

        await NewManager(provider).DownloadAndAttachAsync(MediaId, "9001", "en");

        saved!.ExternalFilePath.Should().StartWith(_store);
        saved.ExternalFilePath.Should().EndWith(".vtt");
    }

    [Fact]
    public async Task A_failed_conversion_attaches_no_track()
    {
        Arrange();
        _extractor.ConvertToWebVttAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var provider = Provider("opensubtitles_search", configured: true);
        provider.DownloadAsync("9001", Arg.Any<CancellationToken>())
            .Returns(new SubtitleDownloadDto { Content = [1, 2, 3], Format = "srt" });

        var result = await NewManager(provider).DownloadAndAttachAsync(MediaId, "9001", "en");

        result.Should().BeNull();
        await _media.DidNotReceive().AddSubtitleTrackAsync(Arg.Any<MediaSubtitleTrack>());
    }

    [Fact]
    public async Task A_provider_that_returns_nothing_attaches_no_track()
    {
        Arrange();
        var provider = Provider("opensubtitles_search", configured: true);
        provider.DownloadAsync("9001", Arg.Any<CancellationToken>()).Returns((SubtitleDownloadDto?)null);

        (await NewManager(provider).DownloadAndAttachAsync(MediaId, "9001", "en")).Should().BeNull();
        await _media.DidNotReceive().AddSubtitleTrackAsync(Arg.Any<MediaSubtitleTrack>());
    }

    // The picker shows Title in preference to Language, so naming the source is
    // what tells a downloaded track apart from the one that shipped in the file.
    [Theory]
    [InlineData("en", "OpenSubtitles", "EN (OpenSubtitles)")]
    [InlineData(null, "OpenSubtitles", "OpenSubtitles")]
    public void A_downloaded_track_is_titled_with_its_source(string? language, string provider, string expected)
    {
        SubtitleSearchManager.BuildTitle(language, provider).Should().Be(expected);
    }
}
