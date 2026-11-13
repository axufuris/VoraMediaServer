using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.Recommendations;
using Vora.Application.Recommendations.ViewModels;
using Vora.Application.Settings;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Recommendations;

// For You asked every provider on every request: the local rows' 65 queries
// each time, and after each restart a chat completion before the AI's. It reads
// the rows each provider last built, and rebuilds them behind the page.
public class RecommendationManagerTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTime Now { get; set; } = new(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }

    private const string LocalId = "local";
    private const string AiId = "ai";

    private readonly IRecommendationRepository _repository = Substitute.For<IRecommendationRepository>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly IRecommendationSnapshotRepository _snapshots = Substitute.For<IRecommendationSnapshotRepository>();
    private readonly IForYouRebuildQueue _queue = Substitute.For<IForYouRebuildQueue>();
    private readonly IRecommendationProvider _local = Substitute.For<IRecommendationProvider>();
    private readonly IRecommendationProvider _ai = Substitute.For<IRecommendationProvider>();
    private readonly Clock _clock = new();
    private readonly Guid _profile = Guid.NewGuid();
    private readonly List<List<Guid>> _asked = new();
    private readonly List<StoredProviderRows> _stored = new();
    private DateTime? _lastActivity;

    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid(), Matched = Guid.NewGuid();

    public RecommendationManagerTests()
    {
        _local.Id.Returns(LocalId);
        _local.MakesOutsideCalls.Returns(false);
        _ai.Id.Returns(AiId);
        _ai.MakesOutsideCalls.Returns(true);
        _local.GetRecommendationsAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(new List<RecommendationListDto>());
        _ai.GetRecommendationsAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(new List<RecommendationListDto>());

        _snapshots.GetAsync(_profile, Arg.Any<Guid?>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<bool>())
            .Returns(call =>
            {
                var ids = call.ArgAt<IReadOnlyCollection<string>>(2);
                var withActivity = call.ArgAt<bool>(3);
                return new StoredRecommendations(_stored.Where(s => ids.Contains(s.ProviderId)).ToList(), withActivity ? _lastActivity : null);
            });

        _repository.GetHydratedMediaItemsAsync(_profile, Arg.Any<List<Guid>>(), Arg.Any<List<string>>(), Arg.Any<Guid?>(), true, Arg.Any<List<Guid>>(), Arg.Any<List<string>>(), Arg.Any<List<string>>(), false)
            .Returns(call =>
            {
                var ids = call.ArgAt<List<Guid>>(1);
                var tmdb = call.ArgAt<List<string>>(2);
                _asked.Add(ids.ToList());
                // Returned in no particular order, as the database does. D is
                // gone, or not the viewer's to see.
                var found = ids.Where(id => id != D).Select(id => new LibraryItemVM { Id = id, Title = id.ToString() }).Reverse().ToList();
                if (tmdb.Contains("tt-1")) found.Add(new LibraryItemVM { Id = Matched, Title = "matched" });
                return found;
            });
    }

    private RecommendationManager Manager(params IRecommendationProvider[] providers) =>
        new(providers.Length == 0 ? new[] { _ai, _local } : providers, _settings, _repository, _snapshots, _queue, _clock, NullLogger<RecommendationManager>.Instance);

    private Task<List<RecommendationListVM>> RowsAsync(string? providerId = null, Guid? libraryId = null) =>
        Manager().GetRecommendationsAsync(_profile, libraryId, true, new(), new(), new(), false, providerId);

    private void Store(string providerId, TimeSpan age, params RecommendationListDto[] rows) =>
        _stored.Add(new StoredProviderRows(providerId, rows.ToList(), _clock.Now - age));

    private static RecommendationListDto Row(string title, int weight, params Guid[] ids) =>
        new() { Title = title, Weight = weight, LocalItemIds = ids.ToList() };

    private static RecommendationListDto AiRow(string title, int weight, params Guid[] ids) =>
        new() { Title = title, Weight = weight, IsAi = true, LocalItemIds = ids.ToList() };

    private Task AskedNothingAsync(IRecommendationProvider provider) =>
        provider.DidNotReceive().GetRecommendationsAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());

    [Fact]
    public async Task Stored_rows_are_served_without_asking_the_provider()
    {
        Store(LocalId, TimeSpan.FromHours(1), Row("Because you watched Heat", 1, A, B));
        Store(AiId, TimeSpan.FromHours(1), AiRow("Gritty Heists", 1, C));

        var rows = await RowsAsync();

        rows.Select(r => r.Title).Should().Equal("Gritty Heists", "Because you watched Heat");
        await AskedNothingAsync(_local);
        await AskedNothingAsync(_ai);
        _queue.DidNotReceiveWithAnyArgs().TryQueue(default);
        await _snapshots.DidNotReceiveWithAnyArgs().SaveAsync(default, default, default!, default!, default);
    }

    // The AI rows' titles began "AI: ". They carry a flag instead, which the
    // clients draw as a small mark beside the title.
    [Fact]
    public async Task A_row_says_whether_the_ai_made_it()
    {
        Store(LocalId, TimeSpan.FromHours(1), Row("Because you watched Heat", 1, A, B));
        Store(AiId, TimeSpan.FromHours(1), AiRow("Gritty Heists", 1, C));

        var rows = await RowsAsync();

        rows.Select(r => (r.Title, r.IsAi)).Should().Equal(("Gritty Heists", true), ("Because you watched Heat", false));
    }

    [Fact]
    public async Task Ai_rows_older_than_twelve_hours_are_still_served_and_a_rebuild_is_queued()
    {
        var library = Guid.NewGuid();
        Store(AiId, TimeSpan.FromHours(12), AiRow("Gritty Heists", 1, A));

        var rows = await RowsAsync(AiId, library);

        rows.Should().ContainSingle().Which.Items.Select(i => i.Id).Should().Equal(A);
        _queue.Received(1).TryQueue(new ForYouRebuild(_profile, library, AiId));
        await AskedNothingAsync(_ai);
    }

    [Fact]
    public async Task Ai_rows_younger_than_twelve_hours_are_left_alone_whatever_was_watched()
    {
        Store(AiId, TimeSpan.FromHours(11.9), AiRow("Gritty Heists", 1, A));
        _lastActivity = _clock.Now;

        await RowsAsync(AiId);

        _queue.DidNotReceiveWithAnyArgs().TryQueue(default);
    }

    [Fact]
    public async Task Local_rows_are_rebuilt_behind_the_page_once_the_profile_has_watched_or_rated_something_since()
    {
        Store(LocalId, TimeSpan.FromMinutes(30), Row("Because you watched Heat", 1, A));
        _lastActivity = _clock.Now - TimeSpan.FromMinutes(5);

        var rows = await RowsAsync(LocalId);

        rows.Select(r => r.Title).Should().Equal("Because you watched Heat");
        _queue.Received(1).TryQueue(new ForYouRebuild(_profile, null, LocalId));
        await AskedNothingAsync(_local);
    }

    [Fact]
    public async Task Local_rows_are_rebuilt_daily_for_the_titles_added_since()
    {
        Store(LocalId, TimeSpan.FromHours(24), Row("Top rated you haven't seen", 1, A));
        _lastActivity = _clock.Now - TimeSpan.FromDays(3);

        await RowsAsync(LocalId);

        _queue.Received(1).TryQueue(new ForYouRebuild(_profile, null, LocalId));
    }

    [Fact]
    public async Task Up_to_date_local_rows_are_not_rebuilt()
    {
        Store(LocalId, TimeSpan.FromHours(23), Row("Top rated you haven't seen", 1, A));
        _lastActivity = _clock.Now - TimeSpan.FromHours(23.5);

        await RowsAsync(LocalId);

        _queue.DidNotReceiveWithAnyArgs().TryQueue(default);
    }

    // The last play and rating are only read when rows built from them are.
    [Fact]
    public async Task The_profiles_last_activity_is_only_asked_for_with_the_local_rows()
    {
        await RowsAsync(AiId);
        await RowsAsync(LocalId);
        await RowsAsync();

        await _snapshots.Received(1).GetAsync(_profile, null, Arg.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { AiId })), false);
        await _snapshots.Received(1).GetAsync(_profile, null, Arg.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { LocalId })), true);
        await _snapshots.Received(1).GetAsync(_profile, null, Arg.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { AiId, LocalId })), true);
    }

    [Fact]
    public async Task Missing_local_rows_are_built_while_the_page_waits_and_stored()
    {
        var library = Guid.NewGuid();
        var built = new List<RecommendationListDto> { Row("Because you watched Heat", 1, A, B) };
        _local.GetRecommendationsAsync(_profile, library, Arg.Any<CancellationToken>()).Returns(built);

        var rows = await RowsAsync(LocalId, library);

        rows.Should().ContainSingle().Which.Items.Select(i => i.Id).Should().Equal(A, B);
        await _snapshots.Received(1).SaveAsync(_profile, library, LocalId, Arg.Is<IReadOnlyList<RecommendationListDto>>(l => l.Single().Title == "Because you watched Heat"), _clock.Now);
        _queue.DidNotReceiveWithAnyArgs().TryQueue(default);
    }

    // A profile that has watched nothing yet reads an empty list until it plays
    // something, rather than building one on every visit.
    [Fact]
    public async Task Missing_local_rows_that_come_back_empty_are_stored_empty()
    {
        (await RowsAsync(LocalId)).Should().BeEmpty();

        await _snapshots.Received(1).SaveAsync(_profile, null, LocalId, Arg.Is<IReadOnlyList<RecommendationListDto>>(l => l.Count == 0), _clock.Now);
    }

    [Fact]
    public async Task Missing_ai_rows_are_queued_and_the_page_gets_none_of_them_this_time()
    {
        Store(LocalId, TimeSpan.FromHours(1), Row("Because you watched Heat", 1, A));

        var rows = await RowsAsync();

        rows.Select(r => r.Title).Should().Equal("Because you watched Heat");
        _queue.Received(1).TryQueue(new ForYouRebuild(_profile, null, AiId));
        await AskedNothingAsync(_ai);
        await _snapshots.DidNotReceiveWithAnyArgs().SaveAsync(default, default, default!, default!, default);
    }

    // A plugin that doesn't say whether it calls out is taken to, so it never
    // holds up the page.
    [Fact]
    public async Task A_plugin_that_does_not_say_is_built_in_the_background()
    {
        var plugin = new SilentPlugin();

        var rows = await Manager(plugin).GetRecommendationsAsync(_profile, null, true, new(), new(), new(), false);

        rows.Should().BeEmpty();
        plugin.Asked.Should().Be(0);
        _queue.Received(1).TryQueue(new ForYouRebuild(_profile, null, SilentPlugin.PluginId));
    }

    [Fact]
    public async Task A_local_build_that_fails_leaves_the_other_providers_rows()
    {
        Store(AiId, TimeSpan.FromHours(1), AiRow("Gritty Heists", 1, A));
        _local.GetRecommendationsAsync(_profile, null, Arg.Any<CancellationToken>())
            .Returns<Task<IEnumerable<RecommendationListDto>>>(_ => throw new InvalidOperationException("boom"));

        var rows = await RowsAsync();

        rows.Select(r => r.Title).Should().Equal("Gritty Heists");
        await _snapshots.DidNotReceiveWithAnyArgs().SaveAsync(default, default, default!, default!, default);
    }

    [Fact]
    public async Task A_provider_switched_off_is_not_read()
    {
        _settings.GetPluginSettingAsync(AiId, "is_enabled").Returns("false");
        Store(AiId, TimeSpan.FromHours(20), AiRow("Gritty Heists", 1, A));
        Store(LocalId, TimeSpan.FromHours(1), Row("Because you watched Heat", 1, B));

        var rows = await RowsAsync();

        rows.Select(r => r.Title).Should().Equal("Because you watched Heat");
        _queue.DidNotReceiveWithAnyArgs().TryQueue(default);
        await _snapshots.Received(1).GetAsync(_profile, null, Arg.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { LocalId })), true);
    }

    [Fact]
    public async Task Every_row_of_library_titles_is_loaded_in_one_query_and_keeps_its_order()
    {
        Store(AiId, TimeSpan.FromHours(1), AiRow("Gritty Heists", 1, C));
        Store(LocalId, TimeSpan.FromHours(1),
            Row("Because you watched Heat", 1, A, B, D),
            Row("Because you like Crime", 2, C, A));

        var rows = await RowsAsync();

        _asked.Should().ContainSingle().Which.Should().BeEquivalentTo(new[] { A, B, D, C });
        rows.Select(r => r.Title).Should().Equal("Gritty Heists", "Because you watched Heat", "Because you like Crime");
        rows[1].Items.Select(i => i.Id).Should().Equal(A, B);
        rows[2].Items.Select(i => i.Id).Should().Equal(C, A);
    }

    [Fact]
    public async Task A_row_named_by_tmdb_ids_is_matched_on_its_own()
    {
        Store(LocalId, TimeSpan.FromHours(1),
            Row("Local", 1, A),
            new RecommendationListDto { Title = "From a plugin", Weight = 2, ExternalTmdbIds = new() { "tt-1" } });

        var rows = await RowsAsync(LocalId);

        _asked.Should().HaveCount(2);
        rows.Select(r => r.Title).Should().Equal("Local", "From a plugin");
        rows[1].Items.Select(i => i.Id).Should().Equal(Matched);
    }

    [Fact]
    public async Task A_row_none_of_whose_titles_can_be_shown_is_left_out()
    {
        Store(LocalId, TimeSpan.FromHours(1), Row("Gone", 1, D), Row("Here", 2, B));

        (await RowsAsync(LocalId)).Select(r => r.Title).Should().Equal("Here");
    }

    [Fact]
    public async Task A_rebuild_stores_what_the_provider_found_as_of_when_it_started()
    {
        var library = Guid.NewGuid();
        _ai.GetRecommendationsAsync(_profile, library, Arg.Any<CancellationToken>()).Returns(new List<RecommendationListDto> { AiRow("Gritty Heists", 1, A) });

        var result = await Manager().RebuildAsync(new ForYouRebuild(_profile, library, AiId), TestContext.Current.CancellationToken);

        result.Should().Be(ForYouRebuildResult.Stored);
        await _snapshots.Received(1).SaveAsync(_profile, library, AiId, Arg.Is<IReadOnlyList<RecommendationListDto>>(l => l.Single().Title == "Gritty Heists"), _clock.Now);
    }

    // Keeping an empty answer for twelve hours meant one bad search hid the AI
    // rows for the rest of the day. It also is what a profile whose account
    // turned AI recommendations off gets, so the old rows go.
    [Fact]
    public async Task An_ai_rebuild_that_finds_nothing_keeps_nothing()
    {
        var result = await Manager().RebuildAsync(new ForYouRebuild(_profile, null, AiId), TestContext.Current.CancellationToken);

        result.Should().Be(ForYouRebuildResult.NothingKept);
        await _snapshots.Received(1).DeleteAsync(_profile, null, AiId);
        await _snapshots.DidNotReceiveWithAnyArgs().SaveAsync(default, default, default!, default!, default);
    }

    [Fact]
    public async Task A_local_rebuild_that_finds_nothing_is_stored_empty()
    {
        var result = await Manager().RebuildAsync(new ForYouRebuild(_profile, null, LocalId), TestContext.Current.CancellationToken);

        result.Should().Be(ForYouRebuildResult.Stored);
        await _snapshots.Received(1).SaveAsync(_profile, null, LocalId, Arg.Is<IReadOnlyList<RecommendationListDto>>(l => l.Count == 0), _clock.Now);
        await _snapshots.DidNotReceiveWithAnyArgs().DeleteAsync(default, default, default!);
    }

    [Fact]
    public async Task A_rebuild_that_fails_touches_nothing_stored()
    {
        _ai.GetRecommendationsAsync(_profile, null, Arg.Any<CancellationToken>())
            .Returns<Task<IEnumerable<RecommendationListDto>>>(_ => throw new InvalidOperationException("AI monthly token limit reached"));

        var rebuild = () => Manager().RebuildAsync(new ForYouRebuild(_profile, null, AiId), TestContext.Current.CancellationToken);

        await rebuild.Should().ThrowAsync<InvalidOperationException>();
        await _snapshots.DidNotReceiveWithAnyArgs().SaveAsync(default, default, default!, default!, default);
        await _snapshots.DidNotReceiveWithAnyArgs().DeleteAsync(default, default, default!);
    }

    [Fact]
    public async Task A_rebuild_for_a_provider_no_longer_installed_does_nothing()
    {
        var result = await Manager().RebuildAsync(new ForYouRebuild(_profile, null, "uninstalled"), TestContext.Current.CancellationToken);

        result.Should().Be(ForYouRebuildResult.NoProvider);
        await _snapshots.DidNotReceiveWithAnyArgs().SaveAsync(default, default, default!, default!, default);
    }

    // A third-party plugin built before MakesOutsideCalls existed.
    private sealed class SilentPlugin : IRecommendationProvider
    {
        public const string PluginId = "someone_elses_recommendations";
        public int Asked;
        public string Id => PluginId;
        public string Name => "Someone else's";
        public string Version => "1.0.0";
        public string Description => "";
        public bool IsSystemPlugin => false;
        public string Type => "Recommendation";
        public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => Array.Empty<PluginSettingDefinitionDto>();

        public Task<IEnumerable<RecommendationListDto>> GetRecommendationsAsync(Guid profileId, Guid? libraryId, CancellationToken cancellationToken = default)
        {
            Asked++;
            return Task.FromResult<IEnumerable<RecommendationListDto>>(new List<RecommendationListDto>());
        }
    }
}
