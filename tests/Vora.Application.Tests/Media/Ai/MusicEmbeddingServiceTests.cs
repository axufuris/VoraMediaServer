using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Ai;
using Vora.Application.Media;
using Vora.Application.Media.Ai;
using Vora.Application.Settings;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Media.Ai;

public class MusicEmbeddingServiceTests
{
    private readonly IMusicRepository _repository = Substitute.For<IMusicRepository>();
    private readonly ITrackProfileService _profiles = Substitute.For<ITrackProfileService>();
    private readonly IOpenAiClient _openAi = Substitute.For<IOpenAiClient>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly Guid _blink = Guid.NewGuid();

    public MusicEmbeddingServiceTests()
    {
        _repository.GetAllArtistTagNamesAsync(Arg.Any<int>()).Returns(new Dictionary<Guid, List<string>> { [_blink] = new() { "pop punk", "punk" } });
        _repository.SaveTrackEmbeddingsAsync(Arg.Any<IReadOnlyList<TrackEmbeddingUpdate>>()).Returns(ci => ci.Arg<IReadOnlyList<TrackEmbeddingUpdate>>().Count);
        _openAi.EmbedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IReadOnlyList<string>>().Select(_ => (float[]?)new[] { 0.1f }).ToList());
    }

    private MusicEmbeddingService Service() => new(_repository, _profiles, _openAi, _settings, new NullTaskProgressReporter(), NullLogger<MusicEmbeddingService>.Instance);

    private void AiPlaylists(bool on)
    {
        _settings.GetSettingsAsync().Returns(new ServerSetting { EnableAiMusicPlaylists = on });
        _openAi.IsConfiguredAsync().Returns(true);
    }

    private TrackDescriptor Song(string title, string? hash = null, string? model = OpenAiClient.EmbeddingModel) => new(
        Guid.NewGuid(), title, "blink-182", "Enema of the State", 1999, "Punk", _blink,
        new List<string> { "playful", "rebellious" }, TrackEnergy.High, new List<string> { "growing up" }, new List<string> { "road trip", "party" }, false,
        hash, model);

    private void Library(params TrackDescriptor[] songs) => _repository.GetTrackDescriptorsAsync().Returns(songs.ToList());

    private IReadOnlyList<IReadOnlyList<string>> SentBatches() =>
        _openAi.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IOpenAiClient.EmbedAsync))
            .Select(c => (IReadOnlyList<string>)c.GetArguments()[1]!)
            .ToList();

    [Fact]
    public async Task Nothing_is_described_or_sent_while_AI_playlists_are_off()
    {
        AiPlaylists(false);

        (await Service().PrepareTracksAsync(TestContext.Current.CancellationToken)).Should().Be(0);

        await _profiles.DidNotReceive().ProfileTracksAsync(Arg.Any<CancellationToken>());
        await _openAi.DidNotReceive().EmbedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Songs_are_described_before_they_are_embedded()
    {
        AiPlaylists(true);
        Library(Song("Adam's Song"));

        await Service().PrepareTracksAsync(TestContext.Current.CancellationToken);

        Received.InOrder(() =>
        {
            _profiles.ProfileTracksAsync(Arg.Any<CancellationToken>());
            _repository.GetTrackDescriptorsAsync();
        });
    }

    [Fact]
    public void A_song_is_embedded_from_its_details_tags_and_profile()
    {
        var text = SongProfile.Describe(Song("What's My Age Again?"), new[] { "pop punk", "punk" });

        text.Should().Be("Song: What's My Age Again?. Artist: blink-182. Album: Enema of the State (1999). Genre: Punk. Tags: pop punk, punk. " +
                         "Mood: playful, rebellious. Energy: high. Themes: growing up. Good for: road trip, party");
    }

    [Fact]
    public async Task Only_songs_whose_description_or_model_changed_are_sent()
    {
        AiPlaylists(true);
        var current = Song("All the Small Things");
        current = current with { EmbeddedHash = SongProfile.Fingerprint(SongProfile.Describe(current, new[] { "pop punk", "punk" })) };
        var changed = Song("Dammit", hash: "stale");
        var oldModel = Song("Josie");
        oldModel = oldModel with { EmbeddedHash = SongProfile.Fingerprint(SongProfile.Describe(oldModel, new[] { "pop punk", "punk" })), EmbeddedModel = "text-embedding-ada-002" };
        var never = Song("Adam's Song", hash: null, model: null);
        Library(current, changed, oldModel, never);

        var embedded = await Service().PrepareTracksAsync(TestContext.Current.CancellationToken);

        embedded.Should().Be(3);
        SentBatches().Single().Should().HaveCount(3).And.NotContain(t => t.Contains("All the Small Things"));
        await _repository.Received(1).SaveTrackEmbeddingsAsync(Arg.Is<IReadOnlyList<TrackEmbeddingUpdate>>(u =>
            u.Count == 3 && u.All(x => x.Model == OpenAiClient.EmbeddingModel && x.SourceHash.Length == 64)));
    }

    [Fact]
    public async Task A_library_already_up_to_date_sends_nothing()
    {
        AiPlaylists(true);
        var song = Song("Adam's Song");
        Library(song with { EmbeddedHash = SongProfile.Fingerprint(SongProfile.Describe(song, new[] { "pop punk", "punk" })) });

        (await Service().PrepareTracksAsync(TestContext.Current.CancellationToken)).Should().Be(0);

        await _openAi.DidNotReceive().EmbedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_batch_that_comes_back_empty_stops_the_run()
    {
        AiPlaylists(true);
        Library(Enumerable.Range(0, MusicEmbeddingService.BatchSize + 5).Select(i => Song($"Song {i}")).ToArray());
        _openAi.EmbedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IReadOnlyList<string>>().Select(_ => (float[]?)null).ToList());

        await Service().PrepareTracksAsync(TestContext.Current.CancellationToken);

        SentBatches().Should().ContainSingle();
        await _repository.DidNotReceive().SaveTrackEmbeddingsAsync(Arg.Any<IReadOnlyList<TrackEmbeddingUpdate>>());
    }

    [Fact]
    public async Task Songs_deleted_while_their_batch_was_embedded_are_not_counted()
    {
        AiPlaylists(true);
        Library(Song("All the Small Things"), Song("Adam's Song"));
        _repository.SaveTrackEmbeddingsAsync(Arg.Any<IReadOnlyList<TrackEmbeddingUpdate>>()).Returns(1);

        (await Service().PrepareTracksAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public void Changing_a_profile_changes_the_fingerprint()
    {
        var song = Song("Adam's Song");
        var before = SongProfile.Fingerprint(SongProfile.Describe(song, Array.Empty<string>()));
        var after = SongProfile.Fingerprint(SongProfile.Describe(song with { Moods = new List<string> { "sad" } }, Array.Empty<string>()));

        after.Should().NotBe(before);
        before.Should().Be(SongProfile.Fingerprint(SongProfile.Describe(song, Array.Empty<string>())));
    }
}
