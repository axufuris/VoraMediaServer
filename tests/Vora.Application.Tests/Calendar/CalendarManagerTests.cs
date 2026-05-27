using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Calendar;
using Vora.Application.Media;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Calendar;

public class CalendarManagerTests
{
    private readonly IMediaRepository _media;
    private readonly IPluginSettingsProvider _settings;

    public CalendarManagerTests()
    {
        _media = Substitute.For<IMediaRepository>();
        _settings = Substitute.For<IPluginSettingsProvider>();
    }

    private CalendarManager Build(params ICalendarProvider[] providers) =>
        new(providers, _media, _settings, NullLogger<CalendarManager>.Instance);

    private static ICalendarProvider MakeProvider(string id, IEnumerable<CalendarEventDto> events)
    {
        var p = Substitute.For<ICalendarProvider>();
        p.Id.Returns(id);
        p.GetEventsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(events);
        return p;
    }

    private static CalendarEventDto Event(
        string id, string title, DateTime release, string mediaType = "Movie",
        string? contentRating = "PG-13", Guid? libraryId = null, string? externalId = null,
        string? externalProviderId = null, string? subtitle = null,
        bool isWatchlisted = false, bool isInLibrary = false) =>
        new()
        {
            Id = id,
            Title = title,
            ReleaseDate = release,
            MediaType = mediaType,
            ContentRating = contentRating ?? "Unrated",
            LibraryId = libraryId,
            ExternalId = externalId,
            ExternalProviderId = externalProviderId,
            SubTitle = subtitle,
            IsWatchlisted = isWatchlisted,
            IsInLibrary = isInLibrary
        };

    private static List<string> All() => new();
    private static (DateTime start, DateTime end) Window() =>
        (new DateTime(2026, 5, 1), new DateTime(2026, 5, 31));

    // ---------- Provider routing ----------

    [Fact]
    public async Task GetCalendarEventsAsync_invokes_all_active_providers()
    {
        var p1 = MakeProvider("p1", new[] { Event("e1", "A", new DateTime(2026, 5, 10)) });
        var p2 = MakeProvider("p2", new[] { Event("e2", "B", new DateTime(2026, 5, 11)) });
        var (start, end) = Window();

        var result = await Build(p1, p2).GetCalendarEventsAsync(
            start, end, true, new List<Guid>(), true, All(), All(), false,
            TestContext.Current.CancellationToken);

        result.Should().HaveCount(2);
        await p1.Received(1).GetEventsAsync(start, end, Arg.Any<CancellationToken>());
        await p2.Received(1).GetEventsAsync(start, end, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCalendarEventsAsync_skips_disabled_providers()
    {
        var enabled = MakeProvider("enabled", new[] { Event("e1", "A", new DateTime(2026, 5, 10)) });
        var disabled = MakeProvider("disabled", new[] { Event("e2", "B", new DateTime(2026, 5, 11)) });
        _settings.GetSettingAsync("disabled", "is_enabled").Returns("false");
        var (start, end) = Window();

        var result = await Build(enabled, disabled).GetCalendarEventsAsync(
            start, end, true, new List<Guid>(), true, All(), All(), false,
            TestContext.Current.CancellationToken);

        result.Should().ContainSingle();
        await disabled.DidNotReceive().GetEventsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCalendarEventsAsync_treats_missing_setting_as_enabled()
    {
        var p = MakeProvider("p", new[] { Event("e1", "A", new DateTime(2026, 5, 10)) });
        _settings.GetSettingAsync(Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);
        var (start, end) = Window();

        var result = await Build(p).GetCalendarEventsAsync(
            start, end, true, new List<Guid>(), true, All(), All(), false,
            TestContext.Current.CancellationToken);

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task GetCalendarEventsAsync_swallows_provider_failure_and_uses_others()
    {
        var failing = Substitute.For<ICalendarProvider>();
        failing.Id.Returns("bad");
        failing.GetEventsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns<Task<IEnumerable<CalendarEventDto>>>(_ => throw new InvalidOperationException("boom"));
        var good = MakeProvider("good", new[] { Event("e1", "A", new DateTime(2026, 5, 10)) });
        var (start, end) = Window();

        var result = await Build(failing, good).GetCalendarEventsAsync(
            start, end, true, new List<Guid>(), true, All(), All(), false,
            TestContext.Current.CancellationToken);

        result.Should().ContainSingle();
    }

    // ---------- Content rating gate ----------

    [Fact]
    public async Task GetCalendarEventsAsync_skips_movie_with_rating_outside_allowlist()
    {
        var p = MakeProvider("p", new[]
        {
            Event("e1", "Family", new DateTime(2026, 5, 10), mediaType: "Movie", contentRating: "G"),
            Event("e2", "Adult", new DateTime(2026, 5, 11), mediaType: "Movie", contentRating: "R")
        });
        var (start, end) = Window();

        var result = (await Build(p).GetCalendarEventsAsync(
            start, end,
            hasAllAccess: true, allowedLibs: new List<Guid>(),
            hasAllRatings: false,
            allowedMovieRatings: new List<string> { "G", "PG" },
            allowedTvRatings: new List<string>(),
            blockUnrated: false,
            TestContext.Current.CancellationToken)).ToList();

        result.Should().ContainSingle();
        result[0].Title.Should().Be("Family");
    }

    [Fact]
    public async Task GetCalendarEventsAsync_uses_tv_rating_list_for_tv_media_type()
    {
        var p = MakeProvider("p", new[]
        {
            Event("e1", "Kids Show", new DateTime(2026, 5, 10), mediaType: "TvShow", contentRating: "TV-Y"),
            Event("e2", "Adult Show", new DateTime(2026, 5, 11), mediaType: "TvShow", contentRating: "TV-MA")
        });
        var (start, end) = Window();

        var result = (await Build(p).GetCalendarEventsAsync(
            start, end,
            hasAllAccess: true, allowedLibs: new List<Guid>(),
            hasAllRatings: false,
            allowedMovieRatings: new List<string>(),
            allowedTvRatings: new List<string> { "TV-Y", "TV-PG" },
            blockUnrated: false,
            TestContext.Current.CancellationToken)).ToList();

        result.Should().ContainSingle();
        result[0].Title.Should().Be("Kids Show");
    }

    [Fact]
    public async Task GetCalendarEventsAsync_unrated_passes_when_block_unrated_false()
    {
        var p = MakeProvider("p", new[]
        {
            Event("e1", "Unknown rating", new DateTime(2026, 5, 10), mediaType: "Movie", contentRating: "Unrated")
        });
        var (start, end) = Window();

        var result = await Build(p).GetCalendarEventsAsync(
            start, end, true, new List<Guid>(),
            hasAllRatings: false,
            allowedMovieRatings: new List<string> { "G" },
            allowedTvRatings: new List<string>(),
            blockUnrated: false,
            TestContext.Current.CancellationToken);

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task GetCalendarEventsAsync_unrated_blocked_when_block_unrated_true()
    {
        var p = MakeProvider("p", new[]
        {
            Event("e1", "Unknown rating", new DateTime(2026, 5, 10), mediaType: "Movie", contentRating: "Unrated")
        });
        var (start, end) = Window();

        var result = await Build(p).GetCalendarEventsAsync(
            start, end, true, new List<Guid>(),
            hasAllRatings: false,
            allowedMovieRatings: new List<string> { "G" },
            allowedTvRatings: new List<string>(),
            blockUnrated: true,
            TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCalendarEventsAsync_all_ratings_short_circuits_to_pass()
    {
        var p = MakeProvider("p", new[]
        {
            Event("e1", "Adult", new DateTime(2026, 5, 10), mediaType: "Movie", contentRating: "NC-17")
        });
        var (start, end) = Window();

        var result = await Build(p).GetCalendarEventsAsync(
            start, end, true, new List<Guid>(),
            hasAllRatings: true,
            allowedMovieRatings: new List<string>(),
            allowedTvRatings: new List<string>(),
            blockUnrated: true,
            TestContext.Current.CancellationToken);

        result.Should().ContainSingle();
    }

    // ---------- Library access gate ----------

    [Fact]
    public async Task GetCalendarEventsAsync_event_with_library_id_filtered_by_allowed_libs()
    {
        var allowedLib = Guid.NewGuid();
        var blockedLib = Guid.NewGuid();
        var p = MakeProvider("p", new[]
        {
            Event("e1", "Allowed", new DateTime(2026, 5, 10), libraryId: allowedLib),
            Event("e2", "Blocked", new DateTime(2026, 5, 11), libraryId: blockedLib)
        });
        var (start, end) = Window();

        var result = (await Build(p).GetCalendarEventsAsync(
            start, end,
            hasAllAccess: false,
            allowedLibs: new List<Guid> { allowedLib },
            true, All(), All(), false,
            TestContext.Current.CancellationToken)).ToList();

        result.Should().ContainSingle();
        result[0].Title.Should().Be("Allowed");
    }

    [Fact]
    public async Task GetCalendarEventsAsync_resolves_tmdb_discovery_event_via_library_map()
    {
        var libraryId = Guid.NewGuid();
        var p = MakeProvider("p", new[]
        {
            Event("e1", "Disc", new DateTime(2026, 5, 10),
                externalId: "12345", externalProviderId: "tmdb_discovery")
        });
        _media.GetLibraryIdsByTmdbIdsAsync(Arg.Any<IEnumerable<string>>())
            .Returns(new Dictionary<string, Guid> { ["12345"] = libraryId });
        var (start, end) = Window();

        var result = (await Build(p).GetCalendarEventsAsync(
            start, end,
            hasAllAccess: false,
            allowedLibs: new List<Guid> { libraryId },
            true, All(), All(), false,
            TestContext.Current.CancellationToken)).ToList();

        result.Should().ContainSingle();
        result[0].LibraryId.Should().Be(libraryId);
        result[0].IsInLibrary.Should().BeTrue();
    }

    [Fact]
    public async Task GetCalendarEventsAsync_blocks_tmdb_event_when_mapped_library_not_in_allowlist()
    {
        var mappedLib = Guid.NewGuid();
        var otherLib = Guid.NewGuid();
        var p = MakeProvider("p", new[]
        {
            Event("e1", "Disc", new DateTime(2026, 5, 10),
                externalId: "12345", externalProviderId: "tmdb_discovery")
        });
        _media.GetLibraryIdsByTmdbIdsAsync(Arg.Any<IEnumerable<string>>())
            .Returns(new Dictionary<string, Guid> { ["12345"] = mappedLib });
        var (start, end) = Window();

        var result = await Build(p).GetCalendarEventsAsync(
            start, end,
            hasAllAccess: false,
            allowedLibs: new List<Guid> { otherLib },
            true, All(), All(), false,
            TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCalendarEventsAsync_unmapped_tmdb_event_still_passes_when_no_library_id()
    {
        // Event has external id but isn't in the local library map → keeps no LibraryId,
        // and PassesLibraryAccess returns true at the end (it just doesn't have a library mapping).
        var p = MakeProvider("p", new[]
        {
            Event("e1", "External", new DateTime(2026, 5, 10),
                externalId: "99999", externalProviderId: "tmdb_discovery")
        });
        _media.GetLibraryIdsByTmdbIdsAsync(Arg.Any<IEnumerable<string>>())
            .Returns(new Dictionary<string, Guid>());
        var (start, end) = Window();

        var result = (await Build(p).GetCalendarEventsAsync(
            start, end,
            hasAllAccess: false,
            allowedLibs: new List<Guid> { Guid.NewGuid() },
            true, All(), All(), false,
            TestContext.Current.CancellationToken)).ToList();

        result.Should().ContainSingle();
    }

    // ---------- Deduplication & merging ----------

    [Fact]
    public async Task GetCalendarEventsAsync_deduplicates_same_external_id_same_date_keeping_in_library_version()
    {
        var sameDay = new DateTime(2026, 5, 10);
        var p1 = MakeProvider("p1", new[]
        {
            Event("e1", "Same", sameDay, externalId: "tt1234", subtitle: "Theatrical", isInLibrary: false)
        });
        var p2 = MakeProvider("p2", new[]
        {
            Event("e2", "Same", sameDay, externalId: "tt1234", subtitle: "Theatrical", isInLibrary: true)
        });
        var (start, end) = Window();

        var result = (await Build(p1, p2).GetCalendarEventsAsync(
            start, end, true, new List<Guid>(), true, All(), All(), false,
            TestContext.Current.CancellationToken)).ToList();

        result.Should().ContainSingle();
        result[0].IsInLibrary.Should().BeTrue();
    }

    [Fact]
    public async Task GetCalendarEventsAsync_keeps_events_with_no_external_id_separately()
    {
        var sameDay = new DateTime(2026, 5, 10);
        var p1 = MakeProvider("p1", new[]
        {
            Event("e1", "First", sameDay, externalId: null),
            Event("e2", "Second", sameDay, externalId: null)
        });
        var (start, end) = Window();

        var result = (await Build(p1).GetCalendarEventsAsync(
            start, end, true, new List<Guid>(), true, All(), All(), false,
            TestContext.Current.CancellationToken)).ToList();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetCalendarEventsAsync_merges_watchlist_dot_into_matching_concrete_event_when_within_two_days()
    {
        var concreteDate = new DateTime(2026, 5, 10);
        var watchlistDate = new DateTime(2026, 5, 11); // 1 day off — within tolerance
        var p = MakeProvider("p", new[]
        {
            Event("e1", "Matrix", concreteDate, externalId: "tt001", subtitle: "Theatrical"),
            Event("e2", "Matrix", watchlistDate, externalId: "tt001", subtitle: "Watchlist", isWatchlisted: true)
        });
        var (start, end) = Window();

        var result = (await Build(p).GetCalendarEventsAsync(
            start, end, true, new List<Guid>(), true, All(), All(), false,
            TestContext.Current.CancellationToken)).ToList();

        result.Should().ContainSingle();
        result[0].IsWatchlisted.Should().BeTrue();
    }

    [Fact]
    public async Task GetCalendarEventsAsync_keeps_watchlist_dot_separate_when_outside_tolerance()
    {
        var concreteDate = new DateTime(2026, 5, 10);
        var distantWatchlist = new DateTime(2026, 5, 20); // 10 days off
        var p = MakeProvider("p", new[]
        {
            Event("e1", "Matrix", concreteDate, externalId: "tt001", subtitle: "Theatrical"),
            Event("e2", "Matrix", distantWatchlist, externalId: "tt001", subtitle: "Watchlist", isWatchlisted: true)
        });
        var (start, end) = Window();

        var result = (await Build(p).GetCalendarEventsAsync(
            start, end, true, new List<Guid>(), true, All(), All(), false,
            TestContext.Current.CancellationToken)).ToList();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetCalendarEventsAsync_keeps_only_watchlist_when_no_concrete_match_exists()
    {
        var p = MakeProvider("p", new[]
        {
            Event("e1", "Pending", new DateTime(2026, 5, 10), externalId: "tt001", subtitle: "Watchlist", isWatchlisted: true)
        });
        var (start, end) = Window();

        var result = (await Build(p).GetCalendarEventsAsync(
            start, end, true, new List<Guid>(), true, All(), All(), false,
            TestContext.Current.CancellationToken)).ToList();

        result.Should().ContainSingle();
        result[0].IsWatchlisted.Should().BeTrue();
    }

    [Fact]
    public async Task GetCalendarEventsAsync_results_sorted_by_release_date_ascending()
    {
        var p = MakeProvider("p", new[]
        {
            Event("e1", "Third", new DateTime(2026, 5, 20)),
            Event("e2", "First", new DateTime(2026, 5, 1)),
            Event("e3", "Second", new DateTime(2026, 5, 10))
        });
        var (start, end) = Window();

        var result = (await Build(p).GetCalendarEventsAsync(
            start, end, true, new List<Guid>(), true, All(), All(), false,
            TestContext.Current.CancellationToken)).ToList();

        result.Select(r => r.Title).Should().Equal("First", "Second", "Third");
    }
}
