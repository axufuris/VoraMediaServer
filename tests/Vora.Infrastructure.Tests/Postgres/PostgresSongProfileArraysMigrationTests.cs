using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Vora.Domain.Entities.Library;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Tests.Postgres;

public sealed class PostgresSongProfileArraysMigrationTests : IAsyncLifetime
{
    private const string BeforeArrays = "20261003010710_RefreshArtistsForBiographies";
    private static readonly Guid OnTheList = Guid.NewGuid();
    private static readonly Guid OffTheList = Guid.NewGuid();

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

    private static string InsertTrack(Guid id, Guid libraryId, string title, string moods) => $"""
        INSERT INTO "MediaItems" ("Id", "MediaType", "Title", "LibraryId", "AddedAt", "LockedFields", "IsAdult",
            "HasMidCreditsStinger", "HasPostCreditsStinger", "VideoThumbnailSpriteCount", "VideoThumbnailIntervalSeconds",
            "VideoThumbnailSpriteColumns", "VideoThumbnailWidth", "VideoThumbnailHeight",
            "Moods", "Themes", "GoodFor", "ProfiledAt")
        VALUES ('{id}', 'Track', '{title}', '{libraryId}', now(), '[]', false, false, false, 0, 0, 0, 0, 0,
            '{moods}', '["partying"]', '["party","road trip"]', now());
        """;

    [Fact]
    public async Task Json_lists_become_arrays_and_songs_with_moods_off_the_list_are_described_again()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var before = NewContext())
        {
            await before.GetService<IMigrator>().MigrateAsync(BeforeArrays, cancellationToken);
            var library = new MediaLibrary { Id = Guid.NewGuid(), Name = "Music", Type = LibraryType.Music, FolderPaths = new List<string> { "/music" } };
            before.Set<MediaLibrary>().Add(library);
            await before.SaveChangesAsync(cancellationToken);

            await before.Database.ExecuteSqlRawAsync(InsertTrack(OnTheList, library.Id, "One More Time", """["euphoric","upbeat"]"""), cancellationToken);
            await before.Database.ExecuteSqlRawAsync(InsertTrack(OffTheList, library.Id, "Digital Love", """["warm","upbeat"]"""), cancellationToken);

            await before.GetService<IMigrator>().MigrateAsync(null, cancellationToken);
        }

        await using var after = NewContext();
        var tracks = await after.Tracks.AsNoTracking().ToDictionaryAsync(t => t.Id, cancellationToken);

        tracks[OnTheList].Moods.Should().Equal("euphoric", "upbeat");
        tracks[OnTheList].Themes.Should().Equal("partying");
        tracks[OnTheList].GoodFor.Should().Equal("party", "road trip");
        tracks[OnTheList].ProfiledAt.Should().NotBeNull();
        tracks[OffTheList].Moods.Should().Equal("warm", "upbeat");
        tracks[OffTheList].ProfiledAt.Should().BeNull();
        (await after.Tracks.CountAsync(t => t.Moods != null && t.Moods.Contains("upbeat"), cancellationToken)).Should().Be(2);
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
