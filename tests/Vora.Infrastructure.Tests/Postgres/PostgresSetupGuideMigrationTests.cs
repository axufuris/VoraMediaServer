using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Tests.Postgres;

public class PostgresSetupGuideMigrationTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    [Fact]
    public async Task A_server_that_already_has_an_admin_does_not_get_the_guide_pushed_at_it()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var legacy = new LegacyDatabase();
        await legacy.BuildQaStateAsync(withSetupGuide: false, cancellationToken);
        await using (var before = legacy.NewContext())
        {
            before.Set<User>().Add(new User { Email = "admin@example.com", DisplayName = "Admin", IsAdmin = true });
            await before.SaveChangesAsync(cancellationToken);
        }

        await legacy.MigrateAsync(cancellationToken);

        await using var after = legacy.NewContext();
        var settings = await after.Set<ServerSetting>().AsNoTracking().SingleAsync(cancellationToken);
        settings.SetupGuideStatus.Should().Be(SetupGuideStatus.Skipped);
        settings.SetupGuideContent.Should().Be(SetupGuideContent.MoviesAndShows | SetupGuideContent.Music);
    }

    [Fact]
    public async Task A_brand_new_server_starts_with_the_guide_not_started()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();

        var settings = await db.Set<ServerSetting>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);

        settings.SetupGuideStatus.Should().Be(SetupGuideStatus.NotStarted);
        settings.SetupGuideStep.Should().BeNull();
    }
}
