using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Ai;
using Vora.Application.Media;
using Vora.Application.Media.Ai;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Media.Ai;

// The chat model names and reads; it never picks a song. Songs come from
// FindNearestTracksAsync with the viewer's access, so these pin who is asked,
// with what, and what is never sent.
public class AiPlaylistServiceTests
{
    private readonly IAiPlaylistRepository _repo = Substitute.For<IAiPlaylistRepository>();
    private readonly IMusicRecommendationRepository _recs = Substitute.For<IMusicRecommendationRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IOpenAiClient _openAi = Substitute.For<IOpenAiClient>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();

    private readonly UserProfile _me = new() { Id = Guid.NewGuid(), Name = "Andy", AiMusicPlaylistsEnabled = true };
    private readonly UserProfile _sam = new() { Id = Guid.NewGuid(), Name = "Sam", AiMusicPlaylistsEnabled = true };
    private static readonly MusicAccessFilter CleanOnly = new() { AllowedRatings = new List<string> { "Clean" } };

    public AiPlaylistServiceTests()
    {
        Server();
        _openAi.IsConfiguredAsync().Returns(true);
        _users.GetProfileByIdAsync(_me.Id).Returns(_me);
        _users.GetProfileByIdAsync(_sam.Id).Returns(_sam);
        _repo.GetPlayedVectorsAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(Enumerable.Range(0, 6).Select(i => new PlayedVector(new[] { 1f, i }, 3)).ToList());
        _repo.FindNearestTracksAsync(Arg.Any<float[]>(), Arg.Any<MusicAccessFilter>(), Arg.Any<AiTrackFilter>(), Arg.Any<int>())
            .Returns(_ => Enumerable.Range(0, 40).Select(i => new AiTrackCandidate(Guid.NewGuid(), $"artist{i % 10}", "/art.jpg")).ToList());
        _openAi.EmbedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IReadOnlyList<string>>().Select(_ => (float[]?)new[] { 0.5f, 0.5f }).ToList());
    }

    private void Server(bool on = true, bool requests = true, int perDay = 10) =>
        _settings.GetSettingsAsync().Returns(new ServerSetting { EnableAiMusicPlaylists = on, EnableForYou = true, EnableAiPlaylistRequests = requests, AiPlaylistRequestsPerDay = perDay });

    private AiPlaylistService Service() => new(_repo, _recs, _users, _openAi, _settings, new NullTaskProgressReporter(), NullLogger<AiPlaylistService>.Instance);

    private const string RequestJson = """{"title":"Cooking Night","why":"Easy grooves.","search":"warm 90s hip hop","avoid":"sad songs","yearFrom":1990,"yearTo":1999,"songs":20}""";

    // ---------- Requests ----------

    [Fact]
    public async Task A_request_becomes_a_playlist_from_the_library_through_the_viewers_controls()
    {
        _openAi.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>()).Returns(RequestJson);

        var result = await Service().CreateFromRequestAsync(_me.Id, "cooking dinner, 90s hip hop, nothing sad", CleanOnly, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(AiOutcome.Made);
        await _repo.Received().FindNearestTracksAsync(Arg.Any<float[]>(), CleanOnly, Arg.Is<AiTrackFilter>(f => f.YearFrom == 1990 && f.YearTo == 1999), Arg.Any<int>());
        await _repo.Received(1).AddRequestAsync(Arg.Is<GeneratedMix>(m =>
            m.Kind == GeneratedMixKind.Requested && m.Name == "Cooking Night" && m.Prompt == "cooking dinner, 90s hip hop, nothing sad"
            && m.TrackOrder.Count == 20), AiPlaylistService.KeepRequests);
        await _openAi.Received(1).EmbedAsync(Arg.Any<string>(), Arg.Is<IReadOnlyList<string>>(l => l.Count == 2 && l[1] == "sad songs"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_more_than_two_songs_by_one_artist()
    {
        _openAi.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>()).Returns(RequestJson);
        GeneratedMix? saved = null;
        await _repo.AddRequestAsync(Arg.Do<GeneratedMix>(m => saved = m), Arg.Any<int>());

        await Service().CreateFromRequestAsync(_me.Id, "anything", CleanOnly, TestContext.Current.CancellationToken);

        saved!.TrackOrder.Should().HaveCount(20);
    }

    [Fact]
    public async Task The_daily_limit_stops_a_request_before_anything_is_sent()
    {
        Server(perDay: 3);
        _repo.CountRequestsSinceAsync(_me.Id, Arg.Any<DateTime>()).Returns(3);

        var result = await Service().CreateFromRequestAsync(_me.Id, "road trip", CleanOnly, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(AiOutcome.LimitReached);
        await _openAi.DidNotReceive().CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>());
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task Requests_need_the_server_the_request_switch_and_the_profile(bool server, bool requests, bool profile)
    {
        Server(on: server, requests: requests);
        _me.AiMusicPlaylistsEnabled = profile;

        var result = await Service().CreateFromRequestAsync(_me.Id, "road trip", CleanOnly, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(AiOutcome.Unavailable);
        await _openAi.DidNotReceive().CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task An_empty_request_is_refused()
    {
        (await Service().CreateFromRequestAsync(_me.Id, "   ", CleanOnly, TestContext.Current.CancellationToken)).Outcome.Should().Be(AiOutcome.Invalid);
    }

    // ---------- Weekly ----------

    private const string WeeklyJson = """
    {"playlists":[
      {"title":"Late Night Drive","why":"Moodier stuff.","search":"moody night driving rock"},
      {"title":"Pop Punk Summer","why":"Loud and fast.","search":"upbeat pop punk"},
      {"title":"Acoustic Hours","why":"Unplugged.","search":"acoustic singer songwriter"},
      {"title":"Throwbacks","why":"2000s.","search":"2000s radio rock"}],
     "bridge":{"title":"Punk to Country","why":"From one favourite to another.","from":"fast punk rock","to":"modern country"}}
    """;

    [Fact]
    public async Task A_due_profile_gets_four_themes_and_a_bridge_with_one_chat_and_one_embedding_call()
    {
        _repo.GetProfilesDueForWeeklyAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<int>()).Returns(new List<Guid> { _me.Id });
        _recs.GetTopArtistsForProfileAsync(_me.Id, Arg.Any<MusicAccessFilter>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(new List<ArtistPlayScore> { new() { ArtistId = Guid.NewGuid(), ArtistName = "blink-182", Score = 10 } });
        _recs.GetGenresForArtistsAsync(Arg.Any<IEnumerable<Guid>>()).Returns(new Dictionary<Guid, List<string>>());
        _openAi.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>()).Returns(WeeklyJson);
        IReadOnlyList<GeneratedMix>? saved = null;
        await _repo.ReplaceWeeklyAsync(_me.Id, Arg.Do<IReadOnlyList<GeneratedMix>>(m => saved = m));

        (await Service().GenerateWeeklyForDueProfilesAsync(false, TestContext.Current.CancellationToken)).Should().Be(1);

        saved!.Count(m => m.Kind == GeneratedMixKind.AiPlaylist).Should().Be(4);
        saved!.Should().ContainSingle(m => m.Kind == GeneratedMixKind.Bridge && m.Name == "Punk to Country");
        saved!.First().Description.Should().Be("Moodier stuff.");
        await _openAi.Received(1).CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>());
        await _openAi.Received(1).EmbedAsync(Arg.Any<string>(), Arg.Is<IReadOnlyList<string>>(l => l.Count == 6), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Weekly_playlists_follow_the_profiles_own_parental_controls()
    {
        _me.AllowedMusicRatings = new List<string> { "Clean" };
        _me.BlockUnratedContent = true;
        _repo.GetProfilesDueForWeeklyAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<int>()).Returns(new List<Guid> { _me.Id });
        _recs.GetTopArtistsForProfileAsync(_me.Id, Arg.Any<MusicAccessFilter>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(new List<ArtistPlayScore> { new() { ArtistId = Guid.NewGuid(), ArtistName = "blink-182", Score = 10 } });
        _recs.GetGenresForArtistsAsync(Arg.Any<IEnumerable<Guid>>()).Returns(new Dictionary<Guid, List<string>>());
        _openAi.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>()).Returns(WeeklyJson);

        await Service().GenerateWeeklyForDueProfilesAsync(false, TestContext.Current.CancellationToken);

        await _repo.Received().FindNearestTracksAsync(Arg.Any<float[]>(),
            Arg.Is<MusicAccessFilter>(a => a.AllowedRatings.SequenceEqual(new[] { "Clean" }) && a.BlockUnratedContent),
            Arg.Any<AiTrackFilter>(), Arg.Any<int>());
        await _repo.DidNotReceive().FindNearestTracksAsync(Arg.Any<float[]>(),
            Arg.Is<MusicAccessFilter>(a => a.AllowedRatings.Count == 0), Arg.Any<AiTrackFilter>(), Arg.Any<int>());
    }

    [Fact]
    public async Task Nothing_is_sent_while_the_server_has_AI_playlists_off()
    {
        Server(on: false);

        (await Service().GenerateWeeklyForDueProfilesAsync(true, TestContext.Current.CancellationToken)).Should().Be(0);

        await _repo.DidNotReceive().GetProfilesDueForWeeklyAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<int>());
    }

    // ---------- Blends ----------

    [Fact]
    public async Task A_blend_needs_no_chat_call_and_uses_the_viewers_controls()
    {
        _repo.GetBlendPartnersAsync(_me.Id).Returns(new List<BlendPartner> { new(_sam.Id, "Sam", null) });

        var result = await Service().CreateBlendAsync(_me.Id, _sam.Id, CleanOnly, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(AiOutcome.Made);
        await _repo.Received(1).ReplaceBlendAsync(Arg.Is<GeneratedMix>(m => m.Kind == GeneratedMixKind.Blend && m.Name == "Andy + Sam" && m.PartnerProfileId == _sam.Id));
        await _repo.Received().FindNearestTracksAsync(Arg.Any<float[]>(), CleanOnly, Arg.Any<AiTrackFilter>(), Arg.Any<int>());
        await _openAi.DidNotReceive().CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>());
    }

    // Someone who switched AI playlists off can't be blended with.
    [Fact]
    public async Task A_profile_that_is_not_a_partner_cannot_be_blended_with()
    {
        _repo.GetBlendPartnersAsync(_me.Id).Returns(new List<BlendPartner>());

        (await Service().CreateBlendAsync(_me.Id, _sam.Id, CleanOnly, TestContext.Current.CancellationToken)).Outcome.Should().Be(AiOutcome.PartnerUnavailable);
    }

    // ---------- Reading ----------

    [Fact]
    public async Task An_opted_out_profile_sees_nothing()
    {
        _me.AiMusicPlaylistsEnabled = false;

        var vm = await Service().GetForProfileAsync(_me.Id);

        vm.Enabled.Should().BeFalse();
        await _repo.DidNotReceive().GetAiMixesAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task A_blend_with_someone_who_has_since_opted_out_is_hidden()
    {
        var stillIn = new GeneratedMix { Name = "Andy + Sam", Kind = GeneratedMixKind.Blend, PartnerProfileId = _sam.Id };
        var optedOut = new GeneratedMix { Name = "Andy + Kid", Kind = GeneratedMixKind.Blend, PartnerProfileId = Guid.NewGuid() };
        _repo.GetAiMixesAsync(_me.Id).Returns(new List<GeneratedMix> { stillIn, optedOut });
        _repo.GetBlendPartnersAsync(_me.Id).Returns(new List<BlendPartner> { new(_sam.Id, "Sam", null) });

        var vm = await Service().GetForProfileAsync(_me.Id);

        vm.Blends.Select(b => b.Name).Should().Equal("Andy + Sam");
        vm.Blends.Single().PartnerName.Should().Be("Sam");
    }

    // ---------- Parsing what the AI sends back ----------

    [Fact]
    public void A_malformed_reply_is_no_plan_rather_than_an_error()
    {
        AiPlaylistService.ParseWeekly("not json").Should().BeNull();
        AiPlaylistService.ParseRequest("{\"title\":\"x\"}").Should().BeNull();
    }

    [Fact]
    public void Titles_are_trimmed_to_size_and_impossible_years_are_ignored()
    {
        var plan = AiPlaylistService.ParseRequest("""{"title":"An extremely long playlist title that goes on and on","search":"x","yearFrom":42,"yearTo":2001}""");

        plan!.Title.Length.Should().BeLessThanOrEqualTo(40);
        plan.YearFrom.Should().BeNull();
        plan.YearTo.Should().Be(2001);
    }

    [Fact]
    public void Vectors_are_blended_and_normalised()
    {
        var v = AiPlaylistService.Blend(new[] { 1f, 0f }, 1f, new[] { 0f, 1f }, 1f);

        v[0].Should().BeApproximately(0.7071f, 0.001f);
        v[1].Should().BeApproximately(0.7071f, 0.001f);
    }
}
