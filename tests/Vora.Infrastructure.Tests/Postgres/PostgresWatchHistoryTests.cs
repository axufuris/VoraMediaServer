using Vora.Application.Streaming;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Streaming;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests.Postgres;

public class PostgresWatchHistoryTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    private sealed class World
    {
        public Guid MovieId { get; } = Guid.NewGuid();
        public Guid AdminProfileId { get; } = Guid.NewGuid();
        public Guid KidsProfileId { get; } = Guid.NewGuid();
        public Guid LibraryId { get; } = Guid.NewGuid();
        private string Tag => MovieId.ToString("N")[..8];

        public async Task SeedAsync(VoraDbContext db)
        {
            var library = new MediaLibrary { Id = LibraryId, Name = "Movies " + MovieId.ToString("N")[..6], Type = LibraryType.Movie, FolderPaths = new List<string> { "/movies" } };
            var movie = new Movie { Id = MovieId, Title = $"Spider-Man: Brand New Day {Tag}", LibraryId = library.Id };
            var part = new MediaPart { FilePath = "/movies/spider-man.mkv", MediaItemId = movie.Id, Duration = TimeSpan.FromSeconds(8700) };
            var account = new User { Email = $"{MovieId:N}@example.com", DisplayName = "Andy" };
            var admin = new UserProfile { Id = AdminProfileId, Name = "AX Media Server", UserId = account.Id };
            var kids = new UserProfile { Id = KidsProfileId, Name = "Kids", UserId = account.Id };
            var device = new ClientDevice { DeviceId = "web-" + MovieId.ToString("N")[..6], ClientName = "Vora Web", DeviceName = "Web Browser", LastIpAddress = "192.168.0.1" };
            db.AddRange(library, movie, part, account, admin, kids, device);

            StreamSession Session(Guid profileId, DateTime startedUtc, double minutes, double pausedSeconds, double start, double end, string strategy = "DirectPlay") => new()
            {
                MediaItemId = movie.Id,
                MediaPartId = part.Id,
                UserId = account.Id,
                UserProfileId = profileId,
                ClientDeviceId = device.Id,
                Strategy = strategy,
                VideoStrategy = "DirectPlay",
                AudioStrategy = "DirectPlay",
                SubtitleStrategy = "None",
                StartedAt = startedUtc,
                LastPingAt = startedUtc.AddMinutes(minutes),
                EndedAt = startedUtc.AddMinutes(minutes),
                TotalPausedDuration = pausedSeconds,
                StartPosition = start,
                CurrentPosition = end,
            };

            db.AddRange(
                Session(AdminProfileId, new DateTime(2026, 10, 6, 3, 16, 54, DateTimeKind.Utc), 158.5, 1314, 0, 8699),
                Session(AdminProfileId, new DateTime(2026, 10, 6, 15, 58, 27, DateTimeKind.Utc), 2.5, 136, 0, 6),
                Session(KidsProfileId, new DateTime(2026, 10, 6, 16, 4, 56, DateTimeKind.Utc), 2.5, 9, 0, 4288, "Transcode"));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        public Task<(List<Application.Streaming.Dtos.HistorySessionDto> Data, int Total)> HistoryAsync(
            VoraDbContext db, TimeZoneInfo zone, Guid? profileId = null, int page = 1, int pageSize = 25,
            IReadOnlyCollection<Guid>? libraryIds = null, IReadOnlyCollection<HistoryDelivery>? deliveries = null) =>
            new StreamRepository(db).GetGroupedHistoryAsync(new StreamHistoryQuery(page, pageSize, Tag, profileId, zone, libraryIds ?? [], deliveries ?? []));
    }

    [Fact]
    public async Task A_late_evening_watch_stays_on_its_own_local_day_and_shows_as_completed()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var world = new World();
        await world.SeedAsync(db);

        var (rows, _) = await world.HistoryAsync(db, Chicago, world.AdminProfileId);

        rows.Should().HaveCount(2);
        var evening = rows.Single(r => r.StartedAt.StartsWith("2026-10-06T03:16", StringComparison.Ordinal));
        evening.Date.Should().Be(evening.StartedAt);
        evening.IsGrouped.Should().BeFalse();
        evening.DurationMinutes.Should().Be(137);
        evening.PausedMinutes.Should().Be(22);
        evening.PercentComplete.Should().BeGreaterThanOrEqualTo(90);
        var reopened = rows.Single(r => r != evening);
        reopened.PercentComplete.Should().Be(0);
    }

    [Fact]
    public async Task A_group_shows_the_furthest_any_of_its_sessions_got_by_watching()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var world = new World();
        await world.SeedAsync(db);

        var (rows, _) = await world.HistoryAsync(db, TimeZoneInfo.Utc, world.AdminProfileId);

        var row = rows.Should().ContainSingle().Subject;
        row.IsGrouped.Should().BeTrue();
        row.PercentComplete.Should().BeGreaterThanOrEqualTo(90);
        row.Date.Should().Be(row.StartedAt);
        row.StartedAt.Should().StartWith("2026-10-06T03:16");
    }

    [Fact]
    public async Task Rows_name_the_profile_and_its_account_and_can_be_narrowed_to_one_profile()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var world = new World();
        await world.SeedAsync(db);

        var (kidsRows, total) = await world.HistoryAsync(db, Chicago, world.KidsProfileId);

        total.Should().Be(1);
        var row = kidsRows.Should().ContainSingle().Subject;
        row.UserName.Should().Be("Kids");
        row.AccountName.Should().Be("Andy");
        row.DurationMinutes.Should().Be(2);
        row.PercentComplete.Should().Be(1);

        var (everyone, _) = await world.HistoryAsync(db, Chicago);
        everyone.Select(r => r.UserName).Should().BeEquivalentTo(["AX Media Server", "AX Media Server", "Kids"]);
    }

    [Fact]
    public async Task Pages_hold_the_most_recent_rows_first_and_report_the_full_total()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var world = new World();
        await world.SeedAsync(db);

        var (first, firstTotal) = await world.HistoryAsync(db, Chicago, page: 1, pageSize: 2);
        var (second, secondTotal) = await world.HistoryAsync(db, Chicago, page: 2, pageSize: 2);
        var (beyond, beyondTotal) = await world.HistoryAsync(db, Chicago, page: 3, pageSize: 2);

        firstTotal.Should().Be(3);
        secondTotal.Should().Be(3);
        beyondTotal.Should().Be(3);
        first.Select(r => (r.UserName, r.StartedAt[..16])).Should().Equal(("Kids", "2026-10-06T16:04"), ("AX Media Server", "2026-10-06T15:58"));
        second.Select(r => (r.UserName, r.StartedAt[..16])).Should().Equal(("AX Media Server", "2026-10-06T03:16"));
        beyond.Should().BeEmpty();
    }

    [Fact]
    public async Task How_a_title_was_played_and_which_library_it_is_in_are_filtered_before_paging()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var world = new World();
        await world.SeedAsync(db);

        var (transcodes, transcodeTotal) = await world.HistoryAsync(db, Chicago, deliveries: [HistoryDelivery.Transcode]);
        var (direct, directTotal) = await world.HistoryAsync(db, Chicago, deliveries: [HistoryDelivery.DirectPlay, HistoryDelivery.DirectStream]);
        var (otherLibrary, otherTotal) = await world.HistoryAsync(db, Chicago, libraryIds: [Guid.NewGuid()]);
        var (ownLibrary, ownTotal) = await world.HistoryAsync(db, Chicago, libraryIds: [world.LibraryId]);

        transcodeTotal.Should().Be(1);
        transcodes.Single().UserName.Should().Be("Kids");
        directTotal.Should().Be(2);
        direct.Should().OnlyContain(r => r.UserName == "AX Media Server");
        otherTotal.Should().Be(0);
        otherLibrary.Should().BeEmpty();
        ownTotal.Should().Be(3);
        ownLibrary.Should().HaveCount(3);
    }
}
