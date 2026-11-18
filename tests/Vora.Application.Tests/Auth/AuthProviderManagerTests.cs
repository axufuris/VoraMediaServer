using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Auth.External;
using Vora.Application.Auth.ViewModels;
using Vora.Application.Settings;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Auth;

public class AuthProviderManagerTests
{
    private const string Origin = "http://192.168.0.5:8080";

    private readonly IAuthProviderRepository _providers = Substitute.For<IAuthProviderRepository>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly IAuthSecretProtector _protector = Substitute.For<IAuthSecretProtector>();
    private readonly ServerSetting _serverSettings = new();
    private readonly AuthProviderManager _manager;

    public AuthProviderManagerTests()
    {
        _manager = new AuthProviderManager(_providers, _settings, _protector, NullLogger<AuthProviderManager>.Instance);
        _settings.GetSettingsAsync().Returns(_serverSettings);
        _settings.GetSettingsForUpdateAsync().Returns(_serverSettings);
        _protector.Protect(Arg.Any<string>()).Returns(call => "protected:" + call.ArgAt<string>(0));
    }

    private static SaveAuthProviderRequest Request(Action<SaveAuthProviderRequest>? change = null)
    {
        var request = new SaveAuthProviderRequest
        {
            Slug = "authentik",
            DisplayName = "Authentik",
            Issuer = "https://auth.example.com/application/o/vora/",
            ClientId = "vora-client",
            ClientSecret = "s3cret",
            Enabled = true
        };
        change?.Invoke(request);
        return request;
    }

    [Fact]
    public async Task A_provider_is_saved_with_its_secret_protected_and_never_returned()
    {
        var result = await _manager.CreateAsync(Request(), Origin);

        result.Outcome.Should().Be(AuthProviderSaveOutcome.Saved);
        result.Provider!.HasSecret.Should().BeTrue();
        System.Text.Json.JsonSerializer.Serialize(result.Provider).Should().NotContain("s3cret").And.NotContain("protected:");
        await _providers.Received(1).AddAsync(Arg.Is<AuthProvider>(p => p.ClientSecretCiphertext == "protected:s3cret" && p.Slug == "authentik" && p.Scopes == "openid email profile"));
    }

    [Fact]
    public async Task The_redirect_address_to_register_is_built_from_the_public_address_or_else_the_request()
    {
        var fromRequest = await _manager.CreateAsync(Request(), Origin);
        _serverSettings.PublicBaseUrl = "https://vora.example.com";
        var fromSetting = await _manager.CreateAsync(Request(r => r.Slug = "second"), Origin);

        fromRequest.Provider!.RedirectUri.Should().Be("http://192.168.0.5:8080/api/auth/oidc/authentik/callback");
        fromSetting.Provider!.RedirectUri.Should().Be("https://vora.example.com/api/auth/oidc/second/callback");
    }

    [Fact]
    public async Task Leaving_the_secret_out_of_an_update_keeps_it_and_sending_a_new_one_replaces_it()
    {
        var existing = new AuthProvider { Slug = "authentik", DisplayName = "Authentik", ClientSecretCiphertext = "protected:old" };
        _providers.GetByIdAsync(existing.Id).Returns(existing);

        await _manager.UpdateAsync(existing.Id, Request(r => r.ClientSecret = null), Origin);
        existing.ClientSecretCiphertext.Should().Be("protected:old");

        await _manager.UpdateAsync(existing.Id, Request(r => r.ClientSecret = "new"), Origin);
        existing.ClientSecretCiphertext.Should().Be("protected:new");

        await _manager.UpdateAsync(existing.Id, Request(r => { r.ClientSecret = null; r.ClearClientSecret = true; }), Origin);
        existing.ClientSecretCiphertext.Should().BeNull();
    }

    [Theory]
    [InlineData("-authentik")]
    [InlineData("authentik-")]
    [InlineData("auth entik")]
    [InlineData("auth/entik")]
    [InlineData("")]
    public async Task A_slug_that_is_not_a_clean_address_name_is_refused(string slug)
    {
        var result = await _manager.CreateAsync(Request(r => r.Slug = slug), Origin);

        result.Outcome.Should().Be(AuthProviderSaveOutcome.Invalid);
        await _providers.DidNotReceive().AddAsync(Arg.Any<AuthProvider>());
    }

    [Theory]
    [InlineData("ftp://auth.example.com")]
    [InlineData("auth.example.com")]
    [InlineData("https://auth.example.com/?x=1")]
    [InlineData("javascript:alert(1)")]
    public async Task An_issuer_that_is_not_a_web_address_is_refused(string issuer)
    {
        var result = await _manager.CreateAsync(Request(r => r.Issuer = issuer), Origin);

        result.Outcome.Should().Be(AuthProviderSaveOutcome.Invalid);
    }

    [Fact]
    public async Task A_provider_cannot_be_turned_on_without_its_issuer_and_client_id()
    {
        var noIssuer = await _manager.CreateAsync(Request(r => r.Issuer = null), Origin);
        var noClient = await _manager.CreateAsync(Request(r => r.ClientId = " "), Origin);
        var draft = await _manager.CreateAsync(Request(r => { r.Issuer = null; r.ClientId = null; r.Enabled = false; }), Origin);

        noIssuer.Outcome.Should().Be(AuthProviderSaveOutcome.Invalid);
        noClient.Outcome.Should().Be(AuthProviderSaveOutcome.Invalid);
        draft.Outcome.Should().Be(AuthProviderSaveOutcome.Saved);
    }

    [Fact]
    public async Task The_scopes_must_include_openid_and_are_tidied()
    {
        var without = await _manager.CreateAsync(Request(r => r.Scopes = "email profile"), Origin);
        var tidy = await _manager.CreateAsync(Request(r => r.Scopes = "openid,email  email groups"), Origin);

        without.Outcome.Should().Be(AuthProviderSaveOutcome.Invalid);
        tidy.Provider!.Scopes.Should().Be("openid email groups");
    }

    [Fact]
    public async Task Ldap_cannot_be_added_yet()
    {
        var result = await _manager.CreateAsync(Request(r => r.Type = AuthProviderType.Ldap), Origin);

        result.Outcome.Should().Be(AuthProviderSaveOutcome.Invalid);
    }

    [Fact]
    public async Task A_slug_another_provider_already_uses_is_refused()
    {
        _providers.GetBySlugAsync("authentik").Returns(new AuthProvider { Slug = "authentik", DisplayName = "Other" });

        var result = await _manager.CreateAsync(Request(), Origin);

        result.Outcome.Should().Be(AuthProviderSaveOutcome.SlugInUse);
        await _providers.DidNotReceive().AddAsync(Arg.Any<AuthProvider>());
    }

    [Fact]
    public async Task Email_domains_are_normalised()
    {
        var result = await _manager.CreateAsync(Request(r => r.AllowedEmailDomains = ["@Example.com", " example.com ", "", "Other.org"]), Origin);

        result.Provider!.AllowedEmailDomains.Should().Equal("example.com", "other.org");
    }

    [Fact]
    public async Task The_public_list_has_only_turned_on_providers_in_order_and_the_sign_in_settings()
    {
        _serverSettings.RegistrationMode = RegistrationMode.Invitation;
        _serverSettings.PasswordSignInEnabled = false;
        _providers.GetEnabledAsync().Returns(
        [
            new AuthProvider { Slug = "b", DisplayName = "Beta", Enabled = true, SortOrder = 2 },
            new AuthProvider { Slug = "a", DisplayName = "Alpha", Enabled = true, SortOrder = 1 },
            new AuthProvider { Slug = "ldap", DisplayName = "Directory", Enabled = true, Type = AuthProviderType.Ldap }
        ]);

        var vm = await _manager.GetPublicAsync();

        vm.Providers.Select(p => p.Slug).Should().Equal("a", "b");
        vm.PasswordSignInEnabled.Should().BeFalse();
        vm.RegistrationMode.Should().Be(RegistrationMode.Invitation);
        System.Text.Json.JsonSerializer.Serialize(vm).Should().NotContain("Secret").And.NotContain("Issuer").And.NotContain("ClientId");
    }

    [Fact]
    public async Task Password_sign_in_cannot_be_turned_off_before_a_provider_is_on()
    {
        _providers.GetEnabledAsync().Returns([]);

        var result = await _manager.UpdateSettingsAsync(new UpdateAuthSettingsRequest { PasswordSignInEnabled = false }, Origin);

        result.Saved.Should().BeFalse();
        _serverSettings.PasswordSignInEnabled.Should().BeTrue();
        await _settings.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task Password_sign_in_can_be_turned_off_once_a_provider_is_on()
    {
        _providers.GetEnabledAsync().Returns([new AuthProvider { Slug = "a", DisplayName = "Alpha", Enabled = true }]);

        var result = await _manager.UpdateSettingsAsync(new UpdateAuthSettingsRequest { PasswordSignInEnabled = false, PublicBaseUrl = " https://vora.example.com/ " }, Origin);

        result.Saved.Should().BeTrue();
        _serverSettings.PasswordSignInEnabled.Should().BeFalse();
        _serverSettings.PublicBaseUrl.Should().Be("https://vora.example.com");
        result.Settings!.EffectiveBaseUrl.Should().Be("https://vora.example.com");
        await _settings.Received(1).SaveChangesAsync();
    }

    [Theory]
    [InlineData("vora.example.com")]
    [InlineData("https://vora.example.com/?a=b")]
    [InlineData("ftp://vora.example.com")]
    public async Task A_public_address_that_is_not_a_web_address_is_refused(string address)
    {
        var result = await _manager.UpdateSettingsAsync(new UpdateAuthSettingsRequest { PasswordSignInEnabled = true, PublicBaseUrl = address }, Origin);

        result.Saved.Should().BeFalse();
    }

    [Fact]
    public async Task The_public_address_falls_back_to_the_email_link_address_before_the_request()
    {
        _serverSettings.EmailPublicBaseUrl = "https://mail.example.com/";

        var settings = await _manager.GetSettingsAsync(Origin);

        settings.EffectiveBaseUrl.Should().Be("https://mail.example.com");
    }

    [Fact]
    public async Task A_provider_that_would_lock_accounts_out_is_not_removed_unless_forced()
    {
        var provider = new AuthProvider { Slug = "a", DisplayName = "Alpha" };
        _providers.GetByIdAsync(provider.Id).Returns(provider);
        _providers.CountUsersWithoutOtherWayInAsync(provider.Id).Returns(3);

        var refused = await _manager.DeleteAsync(provider.Id, force: false);
        await _providers.DidNotReceive().DeleteAsync(provider.Id);
        var forced = await _manager.DeleteAsync(provider.Id, force: true);

        refused.Outcome.Should().Be(AuthProviderDeleteOutcome.WouldStrandUsers);
        refused.StrandedUsers.Should().Be(3);
        forced.Outcome.Should().Be(AuthProviderDeleteOutcome.Deleted);
        await _providers.Received(1).DeleteAsync(provider.Id);
    }

    [Fact]
    public async Task Removing_an_unknown_provider_is_not_found()
    {
        (await _manager.DeleteAsync(Guid.NewGuid(), force: false)).Outcome.Should().Be(AuthProviderDeleteOutcome.NotFound);
    }
}
