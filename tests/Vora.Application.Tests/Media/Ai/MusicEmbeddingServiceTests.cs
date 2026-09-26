using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Ai;
using Vora.Application.Media;
using Vora.Application.Media.Ai;
using Vora.Application.Settings;
using Vora.Domain.Entities.Settings;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Media.Ai;

// Songs are embedded once, only while AI playlists are on, and a batch that
// comes back empty stops the run rather than being paid for again.
public class MusicEmbeddingServiceTests
{
    private readonly IMusicRepository _repository = Substitute.For<IMusicRepository>();
    private readonly IOpenAiClient _openAi = Substitute.For<IOpenAiClient>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();

    private MusicEmbeddingService Service() => new(_repository, _openAi, _settings, new NullTaskProgressReporter(), NullLogger<MusicEmbeddingService>.Instance);

    private void AiPlaylists(bool on)
    {
        _settings.GetSettingsAsync().Returns(new ServerSetting { EnableAiMusicPlaylists = on });
        _openAi.IsConfiguredAsync().Returns(true);
    }

    private static TrackForEmbedding Track(string title) => new(Guid.NewGuid(), title, "blink-182", "Enema of the State", 1999, "Punk");

    [Fact]
    public async Task Nothing_is_sent_while_AI_playlists_are_off()
    {
        AiPlaylists(false);

        (await Service().EmbedMissingTracksAsync(TestContext.Current.CancellationToken)).Should().Be(0);

        await _openAi.DidNotReceive().EmbedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Missing_songs_are_embedded_and_saved_until_none_are_left()
    {
        AiPlaylists(true);
        var batch = new List<TrackForEmbedding> { Track("All the Small Things"), Track("Adam's Song") };
        _repository.GetTracksMissingEmbeddingsAsync(Arg.Any<int>()).Returns(batch, new List<TrackForEmbedding>());
        _openAi.EmbedAsync(MusicEmbeddingService.PluginId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new float[]?[] { new[] { 1f }, new[] { 2f } });

        var done = await Service().EmbedMissingTracksAsync(TestContext.Current.CancellationToken);

        done.Should().Be(2);
        await _repository.Received(1).SaveTrackEmbeddingsAsync(Arg.Is<IReadOnlyList<(Guid, float[])>>(l => l.Count == 2));
    }

    [Fact]
    public async Task A_batch_that_comes_back_empty_stops_the_run()
    {
        AiPlaylists(true);
        _repository.GetTracksMissingEmbeddingsAsync(Arg.Any<int>()).Returns(new List<TrackForEmbedding> { Track("Dammit") });
        _openAi.EmbedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new float[]?[] { null });

        await Service().EmbedMissingTracksAsync(TestContext.Current.CancellationToken);

        await _openAi.Received(1).EmbedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().SaveTrackEmbeddingsAsync(Arg.Any<IReadOnlyList<(Guid, float[])>>());
    }

    [Fact]
    public void A_song_is_described_by_title_artist_album_year_and_genre()
    {
        MusicEmbeddingService.Describe(Track("What's My Age Again?"))
            .Should().Be("Song: What's My Age Again?. Artist: blink-182. Album: Enema of the State (1999). Genre: Punk");
    }
}
