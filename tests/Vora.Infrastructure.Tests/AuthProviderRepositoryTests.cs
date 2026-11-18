using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class AuthProviderRepositoryTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("auth-providers-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static User NewUser(string email, bool hasPassword) =>
        new() { Email = email, DisplayName = email, HasPassword = hasPassword };

    private static AuthProvider NewProvider(string slug, bool enabled = true) =>
        new() { Slug = slug, DisplayName = slug, Enabled = enabled };

    private static UserExternalIdentity Link(User user, AuthProvider provider) =>
        new() { UserId = user.Id, ProviderId = provider.Id, Subject = Guid.NewGuid().ToString() };

    [Fact]
    public async Task Only_accounts_that_sign_in_through_nothing_else_are_counted_as_stranded()
    {
        await using var db = NewContext();
        var repository = new AuthProviderRepository(db);
        var authentik = NewProvider("authentik");
        var keycloak = NewProvider("keycloak");
        var onlyAuthentik = NewUser("a@example.com", hasPassword: false);
        var authentikAndKeycloak = NewUser("b@example.com", hasPassword: false);
        var authentikAndPassword = NewUser("c@example.com", hasPassword: true);
        var onlyKeycloak = NewUser("d@example.com", hasPassword: false);
        db.AddRange(authentik, keycloak, onlyAuthentik, authentikAndKeycloak, authentikAndPassword, onlyKeycloak);
        db.AddRange(
            Link(onlyAuthentik, authentik),
            Link(authentikAndKeycloak, authentik), Link(authentikAndKeycloak, keycloak),
            Link(authentikAndPassword, authentik),
            Link(onlyKeycloak, keycloak));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await repository.CountUsersWithoutOtherWayInAsync(authentik.Id)).Should().Be(1);
        (await repository.CountUsersWithoutOtherWayInAsync(keycloak.Id)).Should().Be(1);
    }

    [Fact]
    public async Task A_provider_with_no_links_strands_nobody()
    {
        await using var db = NewContext();
        var provider = NewProvider("authentik");
        db.Add(provider);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await new AuthProviderRepository(db).CountUsersWithoutOtherWayInAsync(provider.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Providers_are_found_by_slug_and_the_enabled_ones_come_in_order()
    {
        await using var db = NewContext();
        var repository = new AuthProviderRepository(db);
        var second = NewProvider("second");
        second.SortOrder = 2;
        var first = NewProvider("first");
        first.SortOrder = 1;
        var off = NewProvider("off", enabled: false);
        await repository.AddAsync(second);
        await repository.AddAsync(first);
        await repository.AddAsync(off);

        (await repository.GetBySlugAsync("first"))!.Id.Should().Be(first.Id);
        (await repository.GetBySlugAsync("missing")).Should().BeNull();
        (await repository.GetEnabledAsync()).Select(p => p.Slug).Should().Equal("first", "second");
        (await repository.GetAllAsync()).Should().HaveCount(3);
    }

    [Fact]
    public async Task Identities_are_found_by_provider_and_subject_and_listed_with_their_provider()
    {
        await using var db = NewContext();
        var repository = new AuthProviderRepository(db);
        var provider = NewProvider("authentik");
        var user = NewUser("a@example.com", hasPassword: true);
        db.AddRange(provider, user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var identity = Link(user, provider);
        await repository.AddIdentityAsync(identity);

        (await repository.GetIdentityAsync(provider.Id, identity.Subject))!.Id.Should().Be(identity.Id);
        (await repository.GetIdentityAsync(provider.Id, "someone-else")).Should().BeNull();
        (await repository.CountIdentitiesForUserAsync(user.Id)).Should().Be(1);
        (await repository.GetIdentitiesForUserAsync(user.Id)).Single().Provider!.DisplayName.Should().Be("authentik");

        await repository.DeleteIdentityAsync(identity.Id);
        (await repository.CountIdentitiesForUserAsync(user.Id)).Should().Be(0);
    }
}
