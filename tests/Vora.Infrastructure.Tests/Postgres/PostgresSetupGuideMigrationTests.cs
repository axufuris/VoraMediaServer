using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Tests.Postgres;

public sealed class PostgresSetupGuideMigrationTests : IAsyncLifetime
{
    private const string BeforeSetupGuide = "20261003013801_StoreSongProfileListsAsArrays";

    private readonly string _name = "vora_test_" + Guid.NewGuid().ToString("N");
    private string _connectionString = string.Empty;

    public ValueTask InitializeAsync()
    {
        if (PostgresDatabase.IsConfigured)
        {
            _connectionString = new NpgsqlConnectionStringBuilder(PostgresDatabase.ServerConnection) { Database = _name }.ConnectionString;
        }
        return ValueTask.CompletedTask;
    }

    private VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseNpgsql(_connectionString, npgsql => npgsql.UseVector())
            .Options);

    [Fact]
    public async Task A_server_that_already_has_an_admin_does_not_get_the_guide_pushed_at_it()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var before = NewContext())
        {
            await before.GetService<IMigrator>().MigrateAsync(BeforeSetupGuide, cancellationToken);
            before.Set<User>().Add(new User { Email = "admin@example.com", DisplayName = "Admin", IsAdmin = true });
            await before.SaveChangesAsync(cancellationToken);
            await before.GetService<IMigrator>().MigrateAsync(null, cancellationToken);
        }

        await using var after = NewContext();
        var settings = await after.Set<ServerSetting>().AsNoTracking().SingleAsync(cancellationToken);

        settings.SetupGuideStatus.Should().Be(SetupGuideStatus.Skipped);
        settings.SetupGuideContent.Should().Be(SetupGuideContent.MoviesAndShows | SetupGuideContent.Music);
    }

    [Fact]
    public async Task A_brand_new_server_starts_with_the_guide_not_started()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = NewContext();
        await db.Database.MigrateAsync(cancellationToken);

        var settings = await db.Set<ServerSetting>().AsNoTracking().SingleAsync(cancellationToken);

        settings.SetupGuideStatus.Should().Be(SetupGuideStatus.NotStarted);
        settings.SetupGuideStep.Should().BeNull();
    }

    public async ValueTask DisposeAsync()
    {
        if (!PostgresDatabase.IsConfigured) return;

        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(PostgresDatabase.ServerConnection) { Database = "postgres" }.ConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
