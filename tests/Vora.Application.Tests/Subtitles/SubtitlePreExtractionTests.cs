using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Libraries;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Subtitles;
using Vora.Domain.Entities.Settings;

namespace Vora.Application.Tests.Subtitles;

// Pre-extraction warms the subtitle cache after a scan so the on-demand endpoint
// is a file read. It is gated by one setting, skips anything already cached and
// still valid, and never touches image subtitles — those burn into the video and
// have no sidecar.
public class SubtitlePreExtractionTests
{
    private const string TempDir = "/transcode";

    private static readonly Guid ItemId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PartId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TextTrackId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ImageTrackId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly IMediaRepository _media = Substitute.For<IMediaRepository>();
    private readonly ILibraryRepository _libraries = Substitute.For<ILibraryRepository>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly ISubtitleExtractionService _extractor = Substitute.For<ISubtitleExtractionService>();
    private readonly ITranscodeService _transcodes = Substitute.For<ITranscodeService>();

    private readonly string _store = Path.Combine(Path.GetTempPath(), "vora-preextract-store-" + Guid.NewGuid().ToString("N"));

    private SubtitlePreExtractionManager NewManager() => new(
        _media, _libraries, _settings, _extractor, _transcodes,
        new Vora.Plugins.Interfaces.NullTaskProgressReporter(),
        Options.Create(new StoragePathsOptions { Subtitles = _store }),
        NullLogger<SubtitlePreExtractionManager>.Instance);

    private void Arrange(bool enabled = true, bool cached = false, params SubtitleTrackTargetDto[] tracks)
    {
        _settings.GetSettingsAsync().Returns(new ServerSetting
        {
            TranscoderTempDirectory = TempDir,
            PreExtractSubtitlesOnScan = enabled,
        });
        _media.GetSubtitleExtractionTargetsForItemAsync(ItemId).Returns(new List<SubtitleExtractionTargetDto>
        {
            new()
            {
                MediaItemId = ItemId,
                MediaPartId = PartId,
                FilePath = "/media/movie.mkv",
                Tracks = tracks.ToList(),
            }
        });
        _extractor.HasValidCachedWebVtt(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<SubtitleSource>())
            .Returns(cached);
        _transcodes.GetActiveTranscodeCount().Returns(0);
    }

    private static SubtitleTrackTargetDto Text(Guid id, int streamIndex) =>
        new() { Id = id, StreamIndex = streamIndex, Codec = "subrip" };

    private static SubtitleTrackTargetDto Image(Guid id, int streamIndex) =>
        new() { Id = id, StreamIndex = streamIndex, Codec = "hdmv_pgs_subtitle" };

    private Task NothingExtracted() => _extractor.DidNotReceive().GetOrExtractWebVttAsync(
        Arg.Any<SubtitleSource>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());

    [Fact]
    public async Task A_text_track_is_extracted()
    {
        Arrange(tracks: Text(TextTrackId, 3));

        await NewManager().PreExtractForItemAsync(ItemId);

        await _extractor.Received(1).GetOrExtractWebVttAsync(
            SubtitleSource.Embedded("/media/movie.mkv", 3, 0), TempDir, PartId, TextTrackId, Arg.Any<CancellationToken>());
    }

    // The setting is the only switch. It is checked before any work at all, so
    // turning it off costs nothing rather than queueing no-ops.
    [Fact]
    public async Task Nothing_runs_when_the_setting_is_off()
    {
        Arrange(enabled: false, tracks: Text(TextTrackId, 3));

        await NewManager().PreExtractForItemAsync(ItemId);

        await NothingExtracted();
        await _media.DidNotReceive().GetSubtitleExtractionTargetsForItemAsync(Arg.Any<Guid>());
    }

    // Image subtitles are burned into the video by the transcoder; there is no
    // sidecar to produce and extracting one would waste a full file read.
    [Fact]
    public async Task Image_subtitles_are_skipped()
    {
        Arrange(tracks: Image(ImageTrackId, 2));

        await NewManager().PreExtractForItemAsync(ItemId);

        await NothingExtracted();
    }

    [Theory]
    [InlineData("subrip")]
    [InlineData("ass")]
    [InlineData("mov_text")]
    public void Text_codecs_are_extractable(string codec) =>
        SubtitlePreExtractionManager.IsExtractableSubtitleCodec(codec).Should().BeTrue();

    [Theory]
    [InlineData("hdmv_pgs_subtitle")]
    [InlineData("pgssub")]
    [InlineData("dvd_subtitle")]
    [InlineData("vobsub")]
    public void Image_codecs_are_not_extractable(string codec) =>
        SubtitlePreExtractionManager.IsExtractableSubtitleCodec(codec).Should().BeFalse();

    // Idempotency: a second scan of unchanged media must not re-read every file.
    [Fact]
    public async Task An_already_cached_track_is_skipped()
    {
        Arrange(cached: true, tracks: Text(TextTrackId, 3));

        await NewManager().PreExtractForItemAsync(ItemId);

        await NothingExtracted();
    }

    [Fact]
    public async Task The_cache_check_asks_about_the_current_source_and_stream_index()
    {
        Arrange(cached: true, tracks: Text(TextTrackId, 3));

        await NewManager().PreExtractForItemAsync(ItemId);

        _extractor.Received(1).HasValidCachedWebVtt(TempDir, PartId, TextTrackId, SubtitleSource.Embedded("/media/movie.mkv", 3, 0));
    }

    // The retry inside extraction maps by "the Nth subtitle of this file", and
    // ffmpeg counts every subtitle, image ones included. Taking the ordinal
    // after filtering would point the retry at the wrong track.
    [Fact]
    public async Task The_ordinal_counts_image_tracks_that_are_themselves_skipped()
    {
        Arrange(tracks: [Image(ImageTrackId, 2), Text(TextTrackId, 3)]);

        await NewManager().PreExtractForItemAsync(ItemId);

        await _extractor.Received(1).GetOrExtractWebVttAsync(
            Arg.Is<SubtitleSource>(s => s.StreamIndex == 3 && s.Ordinal == 1), Arg.Any<string>(), Arg.Any<Guid>(), TextTrackId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_cancelled_pass_stops_rather_than_finishing_the_list()
    {
        Arrange(tracks: [Text(TextTrackId, 3), Text(Guid.NewGuid(), 4)]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await NewManager().PreExtractForItemAsync(ItemId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await NothingExtracted();
    }

    // Downloaded subtitles live in the persistent store, not the derived cache,
    // so clearing the cache left them on disk forever with nothing referencing
    // them — one orphaned file per subtitle ever fetched for a deleted item.
    [Fact]
    public async Task Deleting_an_item_removes_the_subtitles_downloaded_for_it()
    {
        Arrange(tracks: Text(TextTrackId, 3));
        var itemDirectory = SubtitleStorePath.ItemDirectory(new StoragePathsOptions { Subtitles = _store }, ItemId);
        Directory.CreateDirectory(itemDirectory);
        File.WriteAllText(Path.Combine(itemDirectory, "downloaded.vtt"), "WEBVTT");

        await NewManager().PurgeItemAsync(ItemId);

        Directory.Exists(itemDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task Deleting_an_item_that_downloaded_nothing_is_not_an_error()
    {
        Arrange(tracks: Text(TextTrackId, 3));

        var act = async () => await NewManager().PurgeItemAsync(ItemId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Deleting_an_item_purges_every_part_it_had_cached()
    {
        Arrange(tracks: Text(TextTrackId, 3));

        await NewManager().PurgeItemAsync(ItemId);

        _extractor.Received(1).PurgePart(TempDir, PartId);
    }

    // Purging follows identity, not the setting: turning pre-extraction off must
    // not strand cache entries for media that has since been deleted.
    [Fact]
    public async Task Purging_still_happens_when_the_setting_is_off()
    {
        Arrange(enabled: false, tracks: Text(TextTrackId, 3));

        await NewManager().PurgeItemAsync(ItemId);

        _extractor.Received(1).PurgePart(TempDir, PartId);
    }
}
