using Microsoft.EntityFrameworkCore;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Tests;

public class MigrationHistoryTests
{
    public const string Initial = "20260927181700_Initial";
    public const string ChangesSinceInitial = "20261003013801_StoreSongProfileListsAsArrays";

    [Fact]
    public void The_history_starts_with_initial_then_the_squash_under_the_id_existing_databases_already_have()
    {
        using var db = new VoraDbContext(new DbContextOptionsBuilder<VoraDbContext>()
            .UseNpgsql("Host=unused", npgsql => npgsql.UseVector())
            .Options);

        db.Database.GetMigrations().Take(2).Should().Equal(Initial, ChangesSinceInitial);
    }
}
