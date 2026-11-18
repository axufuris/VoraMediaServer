using Microsoft.EntityFrameworkCore;
using Vora.Application.Users.ViewModels;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Persistence;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class UserSignInMethodsTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("user-sign-in-methods-" + Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task A_person_who_only_signs_in_through_a_provider_says_so_and_names_it()
    {
        using var db = NewContext();
        var authentik = new AuthProvider { Slug = "authentik", DisplayName = "Authentik" };
        var google = new AuthProvider { Slug = "google", DisplayName = "Google" };
        var user = new User { Email = "sso@example.com", DisplayName = "Sso", HasPassword = false };
        db.AddRange(authentik, google, user);
        db.Add(new UserExternalIdentity { UserId = user.Id, ProviderId = google.Id, Subject = "g-1" });
        db.Add(new UserExternalIdentity { UserId = user.Id, ProviderId = authentik.Id, Subject = "a-1" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var vm = await db.Users.Where(u => u.Id == user.Id).Select(UserVM.Projection).SingleAsync(TestContext.Current.CancellationToken);

        Assert.False(vm.HasPassword);
        Assert.Equal(["Authentik", "Google"], vm.SignInProviders);
    }

    [Fact]
    public async Task A_person_with_a_password_and_no_provider_has_an_empty_provider_list()
    {
        using var db = NewContext();
        var user = new User { Email = "local@example.com", DisplayName = "Local" };
        db.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var vm = await db.Users.Where(u => u.Id == user.Id).Select(UserVM.Projection).SingleAsync(TestContext.Current.CancellationToken);

        Assert.True(vm.HasPassword);
        Assert.Empty(vm.SignInProviders);
    }
}
