using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Entities.SmartLists;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Tests.Postgres;

public sealed class PostgresChannelFavoritesMigrationTests : IAsyncLifetime
{
    private const string BeforeSmartListSources = "20261004030256_AddSetupGuide";

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
    public async Task Favorites_saved_per_device_and_per_profile_move_onto_the_profile()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;

        var user = new User { Email = "parent@example.com", DisplayName = "Parent" };
        var playlist = new IptvPlaylist { Id = Guid.NewGuid(), Name = "Cable" };
        var radioList = new IptvPlaylist { Id = Guid.NewGuid(), Name = "Stations", DefaultChannelKind = IptvChannelKind.Radio };
        var cnn = new IptvChannel { Id = Guid.NewGuid(), ExternalChannelId = "cnn.us", Name = "CNN", StreamUrl = "https://example.test/cnn", Kind = IptvChannelKind.Tv, PlaylistId = playlist.Id };
        var bbc = new IptvChannel { Id = Guid.NewGuid(), ExternalChannelId = "bbc.uk", Name = "BBC", StreamUrl = "https://example.test/bbc", Kind = IptvChannelKind.Tv, PlaylistId = playlist.Id };
        var jazz = new IptvChannel { Id = Guid.NewGuid(), ExternalChannelId = "jazz-uuid", Name = "Jazz FM", StreamUrl = "https://example.test/jazz", Kind = IptvChannelKind.Radio, PlaylistId = radioList.Id };
        var profile = new UserProfile { Id = Guid.NewGuid(), Name = "Kid", UserId = user.Id, RadioPrefsJson = $"{{\"favoriteIds\":[\"{jazz.Id.ToString().ToUpperInvariant()}\"]}}" };
        var otherProfile = new UserProfile { Id = Guid.NewGuid(), Name = "Broken prefs", UserId = user.Id, RadioPrefsJson = "{not json" };

        await using (var before = NewContext())
        {
            await before.GetService<IMigrator>().MigrateAsync(BeforeSmartListSources, cancellationToken);
            before.AddRange(user, playlist, radioList, cnn, bbc, jazz, profile, otherProfile);
            before.AddRange(
                new ProfileDeviceSetting { ProfileId = profile.Id, DeviceId = "tv", IptvPrefsJson = "{\"favoriteChannels\":[\"CNN.US\"],\"hideEmpty\":true}" },
                new ProfileDeviceSetting { ProfileId = profile.Id, DeviceId = "phone", IptvPrefsJson = "{\"favoriteChannels\":[\"bbc.uk\",\"cnn.us\",\"gone.channel\"]}" },
                new ProfileDeviceSetting { ProfileId = otherProfile.Id, DeviceId = "tablet", IptvPrefsJson = "[\"provider-1\"]", RadioPrefsJson = "oops" });
            await before.SaveChangesAsync(cancellationToken);
            var holidayId = Guid.NewGuid();
            const string noRules = "{}";
            await before.Database.ExecuteSqlAsync(
                $"INSERT INTO \"SmartLists\" (\"Id\", \"Title\", \"FilterRulesJson\", \"SortBy\", \"MaxItems\", \"DisplayOrder\", \"ShowOnHomepage\", \"ShowToFriends\") VALUES ({holidayId}, 'Holiday Movies', {noRules}, 0, 20, 8, true, true)",
                cancellationToken);
            await before.GetService<IMigrator>().MigrateAsync(null, cancellationToken);
        }

        await using var after = NewContext();
        var favorites = await after.ProfileChannelFavorites.AsNoTracking().ToListAsync(cancellationToken);

        favorites.Where(f => f.ProfileId == profile.Id && f.PlaylistId == playlist.Id).Select(f => f.ExternalChannelId)
            .Should().BeEquivalentTo("cnn.us", "bbc.uk");
        favorites.Where(f => f.PlaylistId == radioList.Id).Select(f => (f.ProfileId, f.ExternalChannelId))
            .Should().Equal((profile.Id, "jazz-uuid"));
        favorites.Should().NotContain(f => f.ProfileId == otherProfile.Id);

        var lists = await after.Set<SmartList>().AsNoTracking().ToListAsync(cancellationToken);
        lists.Where(l => l.Source != SmartListSource.Library).Select(l => l.DisplayOrder).Should().OnlyContain(order => order > 8);
        lists.Where(l => l.DefaultKey != null).Should().HaveCount(11);
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
