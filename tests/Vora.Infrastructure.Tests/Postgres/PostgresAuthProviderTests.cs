using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests.Postgres;

public class PostgresAuthProviderTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private static AuthProvider NewProvider(string slug) => new() { Slug = slug, DisplayName = slug, Enabled = true };

    private static User NewUser(string email, bool hasPassword) =>
        new() { Email = email, DisplayName = email, HasPassword = hasPassword };

    [Fact]
    public async Task A_provider_account_can_be_linked_only_once_and_the_slug_is_unique()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var provider = NewProvider("unique-" + Guid.NewGuid().ToString("N")[..8]);
        var first = NewUser($"{Guid.NewGuid():N}@example.com", true);
        var second = NewUser($"{Guid.NewGuid():N}@example.com", true);
        db.AddRange(provider, first, second);
        db.Add(new UserExternalIdentity { UserId = first.Id, ProviderId = provider.Id, Subject = "same-subject" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var again = database.NewContext();
        again.Add(new UserExternalIdentity { UserId = second.Id, ProviderId = provider.Id, Subject = "same-subject" });
        var link = async () => await again.SaveChangesAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<DbUpdateException>(link);

        await using var duplicateSlug = database.NewContext();
        duplicateSlug.Add(NewProvider(provider.Slug));
        var add = async () => await duplicateSlug.SaveChangesAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<DbUpdateException>(add);
    }

    [Fact]
    public async Task Removing_a_provider_or_a_user_removes_their_links()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var kept = NewProvider("kept-" + Guid.NewGuid().ToString("N")[..8]);
        var removed = NewProvider("removed-" + Guid.NewGuid().ToString("N")[..8]);
        var user = NewUser($"{Guid.NewGuid():N}@example.com", true);
        var leaving = NewUser($"{Guid.NewGuid():N}@example.com", true);
        db.AddRange(kept, removed, user, leaving);
        db.AddRange(
            new UserExternalIdentity { UserId = user.Id, ProviderId = kept.Id, Subject = "a" },
            new UserExternalIdentity { UserId = user.Id, ProviderId = removed.Id, Subject = "b" },
            new UserExternalIdentity { UserId = leaving.Id, ProviderId = kept.Id, Subject = "c" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await new AuthProviderRepository(database.NewContext()).DeleteAsync(removed.Id);
        await using var deleteUser = database.NewContext();
        deleteUser.Remove(await deleteUser.Users.SingleAsync(u => u.Id == leaving.Id, TestContext.Current.CancellationToken));
        await deleteUser.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var check = database.NewContext();
        var subjects = await check.UserExternalIdentities.Where(i => i.UserId == user.Id || i.UserId == leaving.Id).Select(i => i.Subject).ToListAsync(TestContext.Current.CancellationToken);
        subjects.Should().Equal("a");
    }

    [Fact]
    public async Task Accounts_that_could_be_locked_out_are_counted_by_the_database()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var authentik = NewProvider("authentik-" + Guid.NewGuid().ToString("N")[..8]);
        var keycloak = NewProvider("keycloak-" + Guid.NewGuid().ToString("N")[..8]);
        var onlyAuthentik = NewUser($"{Guid.NewGuid():N}@example.com", false);
        var both = NewUser($"{Guid.NewGuid():N}@example.com", false);
        var withPassword = NewUser($"{Guid.NewGuid():N}@example.com", true);
        db.AddRange(authentik, keycloak, onlyAuthentik, both, withPassword);
        db.AddRange(
            new UserExternalIdentity { UserId = onlyAuthentik.Id, ProviderId = authentik.Id, Subject = "1" },
            new UserExternalIdentity { UserId = both.Id, ProviderId = authentik.Id, Subject = "2" },
            new UserExternalIdentity { UserId = both.Id, ProviderId = keycloak.Id, Subject = "3" },
            new UserExternalIdentity { UserId = withPassword.Id, ProviderId = authentik.Id, Subject = "4" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await new AuthProviderRepository(database.NewContext()).CountUsersWithoutOtherWayInAsync(authentik.Id)).Should().Be(1);
    }

    [Fact]
    public async Task The_new_columns_default_for_rows_that_already_existed()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var user = NewUser($"{Guid.NewGuid():N}@example.com", true);
        db.Add(user);
        db.Add(new AuthRefreshToken { FamilyId = Guid.NewGuid(), UserId = user.Id, TokenHash = Guid.NewGuid().ToString("N"), SecurityStamp = "stamp", ExpiresAt = DateTime.UtcNow.AddDays(1) });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var read = database.NewContext();
        (await read.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id, TestContext.Current.CancellationToken)).HasPassword.Should().BeTrue();
        (await read.AuthRefreshTokens.AsNoTracking().SingleAsync(t => t.UserId == user.Id, TestContext.Current.CancellationToken)).SignInMethod.Should().Be(AuthSignInMethod.Password);
    }
}
