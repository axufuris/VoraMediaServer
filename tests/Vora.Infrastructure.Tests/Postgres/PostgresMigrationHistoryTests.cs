using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.SmartLists;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Tests.Postgres;

public class PostgresMigrationHistoryTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    [Fact]
    public async Task A_database_in_qa_state_takes_only_the_collapsed_migration_and_keeps_its_settings()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        await legacy.BuildQaStateAsync(withSetupGuide: false, cancellationToken);
        await using (var before = legacy.NewContext())
        {
            await before.Database.ExecuteSqlRawAsync("UPDATE \"ServerSettings\" SET \"AiPlaylistMatchWindow\" = 0.07", cancellationToken);
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        var settings = await after.Set<ServerSetting>().AsNoTracking().SingleAsync(cancellationToken);
        settings.AiPlaylistMatchWindow.Should().Be(0.07);
        (await after.ProfileChannelFavorites.CountAsync(cancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task A_database_on_the_earlier_combined_migration_only_gains_the_task_table()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        await legacy.BuildEarlierCombinedStateAsync(cancellationToken);

        var user = new User { Email = "parent@example.com", DisplayName = "Parent" };
        var profile = new UserProfile { Id = Guid.NewGuid(), Name = "Kid", UserId = user.Id };
        var playlist = new IptvPlaylist { Id = Guid.NewGuid(), Name = "Cable" };
        List<(Guid Id, int Order, string? Key)> listsBefore;
        await using (var before = legacy.NewContext())
        {
            before.AddRange(user, profile, playlist, new ProfileChannelFavorite { ProfileId = profile.Id, PlaylistId = playlist.Id, ExternalChannelId = "cnn.us" });
            await before.SaveChangesAsync(cancellationToken);
            await before.Database.ExecuteSqlRawAsync("UPDATE \"SmartLists\" SET \"DisplayOrder\" = 0 WHERE \"DefaultKey\" = 'favorite-channels'", cancellationToken);
            (await before.Database.GetPendingMigrationsAsync(cancellationToken)).Should().Equal(MigrationHistoryTests.ChangesSinceInitial);
            listsBefore = (await before.Set<SmartList>().AsNoTracking().ToListAsync(cancellationToken))
                .Select(l => (l.Id, l.DisplayOrder, l.DefaultKey)).OrderBy(l => l.Id).ToList();
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        (await after.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        (await after.Set<SmartList>().AsNoTracking().ToListAsync(cancellationToken))
            .Select(l => (l.Id, l.DisplayOrder, l.DefaultKey)).OrderBy(l => l.Id).ToList()
            .Should().Equal(listsBefore);
        (await after.ProfileChannelFavorites.CountAsync(cancellationToken)).Should().Be(1);
        (await after.PendingTasks.CountAsync(cancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task A_database_that_already_ran_the_setup_guide_migration_keeps_its_guide_progress()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        await legacy.BuildQaStateAsync(withSetupGuide: true, cancellationToken);
        await using (var before = legacy.NewContext())
        {
            before.Set<User>().Add(new User { Email = "admin@example.com", DisplayName = "Admin", IsAdmin = true });
            await before.SaveChangesAsync(cancellationToken);
            await before.Database.ExecuteSqlRawAsync("UPDATE \"ServerSettings\" SET \"SetupGuideStatus\" = 3, \"SetupGuideContent\" = 5", cancellationToken);
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        var settings = await after.Set<ServerSetting>().AsNoTracking().SingleAsync(cancellationToken);
        settings.SetupGuideStatus.Should().Be(SetupGuideStatus.Completed);
        settings.SetupGuideContent.Should().Be(SetupGuideContent.MoviesAndShows | SetupGuideContent.LiveTv);
    }

    [Fact]
    public async Task A_new_server_keeps_its_seeded_settings_with_the_ai_match_window_at_its_default()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();

        var settings = await db.Set<ServerSetting>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);

        settings.Id.Should().Be("GLOBAL_SETTINGS");
        settings.AiPlaylistMatchWindow.Should().Be(0.04);
    }
}
