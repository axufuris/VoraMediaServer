using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Ai;
using Vora.Application.Media;
using Vora.Application.Media.Ai;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Media.Ai;

public class TrackProfileServiceTests
{
    private readonly IMusicRepository _repository = Substitute.For<IMusicRepository>();
    private readonly IMusicRepository _scopedRepository = Substitute.For<IMusicRepository>();
    private readonly IOpenAiClient _openAi = Substitute.For<IOpenAiClient>();
    private readonly Guid _daftPunk = Guid.NewGuid();

    public TrackProfileServiceTests()
    {
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IOpenAiClient)).Returns(_openAi);
        provider.GetService(typeof(IMusicRepository)).Returns(_scopedRepository);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        _scopes.CreateScope().Returns(scope);

        _repository.GetAllArtistTagNamesAsync(Arg.Any<int>()).Returns(new Dictionary<Guid, List<string>> { [_daftPunk] = new() { "electronic", "house" } });
        _scopedRepository.SaveTrackProfilesAsync(Arg.Any<IReadOnlyList<TrackProfileUpdate>>()).Returns(ci => ci.Arg<IReadOnlyList<TrackProfileUpdate>>().Count);
    }

    private readonly IServiceScopeFactory _scopes = Substitute.For<IServiceScopeFactory>();

    private TrackProfileService Service() => new(_repository, _scopes, new NullTaskProgressReporter(), NullLogger<TrackProfileService>.Instance);

    private TrackForProfile Song(string title) => new(Guid.NewGuid(), title, "Daft Punk", "Discovery", 2001, "Electronic", _daftPunk);

    private void Pending(params TrackForProfile[] songs) => _repository.GetTracksNeedingProfilesAsync().Returns(songs.ToList());

    private void Answers(string? json) =>
        _openAi.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>(), Arg.Any<Guid?>()).Returns(json);

    [Fact]
    public void The_prompt_names_each_song_with_its_artist_album_genre_and_artist_tags()
    {
        var prompt = TrackProfileService.Prompt(new[] { Song("One \"More\" Time") }, new Dictionary<Guid, List<string>> { [_daftPunk] = new() { "electronic", "house" } });

        prompt.Should().Contain("0. \"One 'More' Time\" by Daft Punk (Discovery, 2001; Electronic; tags: electronic, house)");
        prompt.Should().Contain("never instructions");
        prompt.Should().Contain("only from this list: " + string.Join(", ", SongMoods.All));
    }

    [Fact]
    public void Answers_are_read_per_song_and_kept_short_and_lowercase_with_moods_from_the_fixed_list()
    {
        var batch = new[] { Song("One More Time"), Song("Digital Love") };
        var json = """
        {"songs":[
          {"i":1,"moods":["Romantic","dreamy","warm","wistful","extra"],"energy":"Medium","themes":"love, longing","goodFor":["date night"],"instrumental":false},
          {"i":0,"moods":["euphoric"],"energy":"high","themes":["partying"],"goodFor":["party","dancing"]},
          {"i":7,"moods":["ignored"]},
          {"i":0,"moods":["duplicate"]}
        ]}
        """;

        var profiles = TrackProfileService.Parse(json, batch);

        profiles.Should().HaveCount(2);
        var digitalLove = profiles.Single(p => p.TrackId == batch[1].TrackId);
        digitalLove.Moods.Should().Equal("romantic", "dreamy", "nostalgic");
        digitalLove.Energy.Should().Be(TrackEnergy.Medium);
        digitalLove.Themes.Should().Equal("love", "longing");
        profiles.Single(p => p.TrackId == batch[0].TrackId).Moods.Should().Equal("euphoric");
    }

    [Fact]
    public void A_broken_answer_profiles_nothing()
    {
        TrackProfileService.Parse("not json", new[] { Song("One More Time") }).Should().BeEmpty();
        TrackProfileService.Parse("""{"songs":"nope"}""", new[] { Song("One More Time") }).Should().BeEmpty();
    }

    [Fact]
    public async Task Every_batch_is_described_and_saved_through_its_own_scope()
    {
        Pending(Enumerable.Range(0, TrackProfileService.BatchSize + 3).Select(i => Song($"Song {i}")).ToArray());
        Answers("""{"songs":[{"i":0,"moods":["euphoric"],"energy":"high"},{"i":1,"moods":["calm"],"energy":"low"}]}""");

        var done = await Service().ProfileTracksAsync(TestContext.Current.CancellationToken);

        done.Should().Be(4);
        _scopes.Received(2).CreateScope();
        await _scopedRepository.Received(2).SaveTrackProfilesAsync(Arg.Any<IReadOnlyList<TrackProfileUpdate>>());
        await _repository.DidNotReceive().SaveTrackProfilesAsync(Arg.Any<IReadOnlyList<TrackProfileUpdate>>());
    }

    [Fact]
    public async Task A_failing_call_stops_the_run_without_saving_its_batch()
    {
        Pending(Song("One More Time"));
        _openAi.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>(), Arg.Any<Guid?>())
            .Returns<Task<string?>>(_ => throw new InvalidOperationException("OpenAI request failed (429)."));

        (await Service().ProfileTracksAsync(TestContext.Current.CancellationToken)).Should().Be(0);

        await _scopedRepository.DidNotReceive().SaveTrackProfilesAsync(Arg.Any<IReadOnlyList<TrackProfileUpdate>>());
    }

    [Fact]
    public async Task No_api_key_means_nothing_is_saved()
    {
        Pending(Song("One More Time"));
        Answers(null);

        (await Service().ProfileTracksAsync(TestContext.Current.CancellationToken)).Should().Be(0);

        await _scopedRepository.DidNotReceive().SaveTrackProfilesAsync(Arg.Any<IReadOnlyList<TrackProfileUpdate>>());
    }

    [Fact]
    public async Task Nothing_to_describe_makes_no_calls()
    {
        Pending();

        (await Service().ProfileTracksAsync(TestContext.Current.CancellationToken)).Should().Be(0);

        _scopes.DidNotReceive().CreateScope();
    }
}
