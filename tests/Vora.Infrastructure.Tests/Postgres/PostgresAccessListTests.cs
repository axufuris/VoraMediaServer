using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Vora.Application.Posters;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Backups;
using Vora.Infrastructure.Backups.Sections;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Vora.Infrastructure.Tests.Backups;

namespace Vora.Infrastructure.Tests.Postgres;

public class PostgresAccessListTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private static MediaLibrary Library(string name) =>
        new() { Name = name, Type = LibraryType.Movie, FolderPaths = new List<string> { "/media/" + name } };

    private static User Account(string name, List<Guid> libraries, List<Guid> playlists) => new()
    {
        Email = $"{name}-{Guid.NewGuid():N}@example.com",
        DisplayName = name,
        HasAllLibraryAccess = false,
        AllowedLibraryIds = libraries,
        HasAllIptvAccess = false,
        AllowedIptvPlaylistIds = playlists
    };

    private static UserProfile Profile(User account, string name, List<Guid> libraries, List<Guid> playlists) => new()
    {
        Name = name,
        UserId = account.Id,
        HasAllLibraryAccess = false,
        AllowedLibraryIds = libraries,
        HasAllIptvAccess = false,
        AllowedIptvPlaylistIds = playlists
    };

    private static LibraryRepository Libraries(VoraDbContext db) => new(db, Substitute.For<IOverlaySweepService>());

    private static Task<string> StoredLibraryListAsync(VoraDbContext db, Guid profileId, CancellationToken cancellationToken) =>
        db.Database.SqlQuery<string>($"SELECT \"AllowedLibraryIds\" AS \"Value\" FROM \"UserProfiles\" WHERE \"Id\" = {profileId}").SingleAsync(cancellationToken);

    [Fact]
    public async Task Deleting_a_library_takes_it_out_of_every_account_and_profile_access_list()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var movies = Library("Movies");
        var kids = Library("Kids");
        var docs = Library("Documentaries");
        var account = Account("Sam", new List<Guid> { movies.Id, kids.Id, docs.Id }, new List<Guid>());
        var profile = Profile(account, "Sam", new List<Guid> { docs.Id, kids.Id }, new List<Guid>());
        var other = Profile(account, "Sam (docs)", new List<Guid> { docs.Id, movies.Id }, new List<Guid>());
        await using (var seed = database.NewContext())
        {
            seed.AddRange(movies, kids, docs, account, profile, other);
            seed.Add(new Movie { Title = "Paddington", LibraryId = kids.Id });
            await seed.SaveChangesAsync(cancellationToken);
        }
        string otherBefore;
        await using (var before = database.NewContext())
        {
            otherBefore = await StoredLibraryListAsync(before, other.Id, cancellationToken);
        }

        await using (var db = database.NewContext())
        {
            await Libraries(db).DeleteLibraryAsync(kids.Id, cancellationToken);
        }

        await using var after = database.NewContext();
        (await after.MediaLibraries.AnyAsync(l => l.Id == kids.Id, cancellationToken)).Should().BeFalse();
        (await after.MediaItems.AnyAsync(m => m.LibraryId == kids.Id, cancellationToken)).Should().BeFalse();
        (await after.Users.AsNoTracking().SingleAsync(u => u.Id == account.Id, cancellationToken)).AllowedLibraryIds.Should().Equal(movies.Id, docs.Id);
        (await after.UserProfiles.AsNoTracking().SingleAsync(p => p.Id == profile.Id, cancellationToken)).AllowedLibraryIds.Should().Equal(docs.Id);
        (await StoredLibraryListAsync(after, profile.Id, cancellationToken)).Should().Be(JsonSerializer.Serialize(new List<Guid> { docs.Id }));
        (await StoredLibraryListAsync(after, other.Id, cancellationToken)).Should().Be(otherBefore);
    }

    [Fact]
    public async Task A_profile_whose_only_library_is_deleted_is_left_with_no_libraries_not_all_of_them()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var cartoons = Library("Cartoons");
        var films = Library("Films");
        var account = Account("Kid", new List<Guid> { cartoons.Id }, new List<Guid>());
        var profile = Profile(account, "Kid", new List<Guid> { cartoons.Id }, new List<Guid>());
        await using (var seed = database.NewContext())
        {
            seed.AddRange(cartoons, films, account, profile);
            await seed.SaveChangesAsync(cancellationToken);
        }

        await using (var db = database.NewContext())
        {
            await Libraries(db).DeleteLibraryAsync(cartoons.Id, cancellationToken);
        }

        await using var after = database.NewContext();
        var stored = await after.UserProfiles.AsNoTracking().SingleAsync(p => p.Id == profile.Id, cancellationToken);
        stored.HasAllLibraryAccess.Should().BeFalse();
        stored.AllowedLibraryIds.Should().BeEmpty();
        (await StoredLibraryListAsync(after, profile.Id, cancellationToken)).Should().Be("[]");
        var visible = await Libraries(after).GetAllProjectedAsync(l => l.Id, stored.HasAllLibraryAccess, stored.AllowedLibraryIds);
        visible.Should().BeEmpty();
    }

    [Fact]
    public async Task A_library_delete_that_fails_leaves_the_library_its_titles_and_the_access_lists_as_they_were()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var anime = Library("Anime");
        var account = Account("Alex", new List<Guid> { anime.Id }, new List<Guid>());
        var profile = Profile(account, "Alex", new List<Guid> { anime.Id }, new List<Guid>());
        await using (var seed = database.NewContext())
        {
            seed.AddRange(anime, account, profile);
            seed.Add(new Movie { Title = "Spirited Away", LibraryId = anime.Id });
            await seed.SaveChangesAsync(cancellationToken);
        }

        await using (var db = database.NewContext(new FailingSave()))
        {
            var delete = () => Libraries(db).DeleteLibraryAsync(anime.Id, cancellationToken);
            await delete.Should().ThrowAsync<InvalidOperationException>();
        }

        await using var after = database.NewContext();
        (await after.MediaLibraries.AnyAsync(l => l.Id == anime.Id, cancellationToken)).Should().BeTrue();
        (await after.MediaItems.CountAsync(m => m.LibraryId == anime.Id, cancellationToken)).Should().Be(1);
        (await after.Users.AsNoTracking().SingleAsync(u => u.Id == account.Id, cancellationToken)).AllowedLibraryIds.Should().Equal(anime.Id);
        (await after.UserProfiles.AsNoTracking().SingleAsync(p => p.Id == profile.Id, cancellationToken)).AllowedLibraryIds.Should().Equal(anime.Id);
    }

    [Fact]
    public async Task Deleting_an_iptv_playlist_takes_it_out_of_every_account_and_profile_access_list()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var freeview = new IptvPlaylist { Name = "Freeview" };
        var sports = new IptvPlaylist { Name = "Sports" };
        var movies = Library("Cinema");
        var account = Account("Jo", new List<Guid> { movies.Id }, new List<Guid> { sports.Id, freeview.Id });
        var profile = Profile(account, "Jo", new List<Guid> { movies.Id }, new List<Guid> { sports.Id });
        await using (var seed = database.NewContext())
        {
            seed.AddRange(freeview, sports, movies, account, profile);
            await seed.SaveChangesAsync(cancellationToken);
        }

        await using (var db = database.NewContext())
        {
            await new IptvRepository(db).DeletePlaylistAsync(sports.Id);
        }

        await using var after = database.NewContext();
        (await after.IptvPlaylists.AnyAsync(p => p.Id == sports.Id, cancellationToken)).Should().BeFalse();
        var storedAccount = await after.Users.AsNoTracking().SingleAsync(u => u.Id == account.Id, cancellationToken);
        storedAccount.AllowedIptvPlaylistIds.Should().Equal(freeview.Id);
        storedAccount.AllowedLibraryIds.Should().Equal(movies.Id);
        var storedProfile = await after.UserProfiles.AsNoTracking().SingleAsync(p => p.Id == profile.Id, cancellationToken);
        storedProfile.AllowedIptvPlaylistIds.Should().BeEmpty();
        storedProfile.HasAllIptvAccess.Should().BeFalse();
        storedProfile.AllowedLibraryIds.Should().Equal(movies.Id);
    }

    [Fact]
    public async Task Restoring_iptv_playlists_takes_the_playlists_it_removes_out_of_the_access_lists()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var restoreDatabase = new PostgresDatabase();
        await restoreDatabase.InitializeAsync();
        try
        {
            var freeview = new IptvPlaylist { Name = "Freeview" };
            var archive = new MemoryBackupArchive();
            await using (var db = restoreDatabase.NewContext())
            {
                db.Add(freeview);
                await db.SaveChangesAsync(cancellationToken);
                await new IptvPlaylistsBackupSection(db).WriteAsync(archive, cancellationToken);
            }

            var addedSince = new IptvPlaylist { Name = "Added after the backup" };
            var account = Account("Pat", new List<Guid>(), new List<Guid> { freeview.Id, addedSince.Id });
            var profile = Profile(account, "Pat", new List<Guid>(), new List<Guid> { addedSince.Id });
            await using (var db = restoreDatabase.NewContext())
            {
                db.AddRange(addedSince, account, profile);
                await db.SaveChangesAsync(cancellationToken);
            }

            await using (var db = restoreDatabase.NewContext())
            {
                await using var transaction = await new EfBackupTransactionFactory(db).BeginAsync(cancellationToken);
                await new IptvPlaylistsBackupSection(db).ReadAsync(archive, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            await using var after = restoreDatabase.NewContext();
            (await after.IptvPlaylists.Select(p => p.Id).ToListAsync(cancellationToken)).Should().Equal(freeview.Id);
            (await after.Users.AsNoTracking().SingleAsync(u => u.Id == account.Id, cancellationToken)).AllowedIptvPlaylistIds.Should().Equal(freeview.Id);
            (await after.UserProfiles.AsNoTracking().SingleAsync(p => p.Id == profile.Id, cancellationToken)).AllowedIptvPlaylistIds.Should().BeEmpty();
        }
        finally
        {
            await restoreDatabase.DisposeAsync();
        }
    }

    private sealed class FailingSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The library row could not be deleted.");
    }
}
