using Microsoft.EntityFrameworkCore;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Tests;

public class MigrationHistoryTests
{
    public const string Initial = "20260927181700_Initial";
    public const string ChangesSinceInitial = "20261004192607_ChangesSinceInitial";

    [Fact]
    public void The_history_is_initial_then_one_migration_with_everything_since()
    {
        using var db = new VoraDbContext(new DbContextOptionsBuilder<VoraDbContext>()
            .UseNpgsql("Host=unused", npgsql => npgsql.UseVector())
            .Options);

        db.Database.GetMigrations().Should().Equal(Initial, ChangesSinceInitial);
    }
}
