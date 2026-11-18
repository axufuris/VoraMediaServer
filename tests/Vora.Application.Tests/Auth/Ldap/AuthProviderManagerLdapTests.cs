using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Auth.External;
using Vora.Application.Auth.ViewModels;
using Vora.Application.Settings;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Auth.Ldap;

public class AuthProviderManagerLdapTests
{
    private const string Origin = "http://192.168.0.5:8080";

    private readonly IAuthProviderRepository _providers = Substitute.For<IAuthProviderRepository>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly IAuthSecretProtector _protector = Substitute.For<IAuthSecretProtector>();
    private readonly ServerSetting _serverSettings = new();
    private readonly AuthProviderManager _manager;

    public AuthProviderManagerLdapTests()
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
            Type = AuthProviderType.Ldap,
            Slug = "directory",
            DisplayName = "Company directory",
            Enabled = true,
            LdapHost = "ldap.example.org",
            LdapSecurity = LdapSecurityMode.Ldaps,
            LdapBindDn = "cn=svc,dc=example,dc=org",
            LdapBindPassword = "bind-secret",
            LdapUserBaseDn = "ou=people,dc=example,dc=org",
            LdapUserFilter = "(&(objectClass=person)(|(uid={username})(mail={username})))"
        };
        change?.Invoke(request);
        return request;
    }

    private static string CertificatePem()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Test CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return certificate.ExportCertificatePem();
    }

    [Fact]
    public async Task A_directory_is_saved_with_its_bind_password_protected_and_never_returned()
    {
        var result = await _manager.CreateAsync(Request(), Origin);

        result.Outcome.Should().Be(AuthProviderSaveOutcome.Saved);
        result.Provider!.Type.Should().Be(AuthProviderType.Ldap);
        result.Provider.HasBindPassword.Should().BeTrue();
        result.Provider.LdapHost.Should().Be("ldap.example.org");
        System.Text.Json.JsonSerializer.Serialize(result.Provider).Should().NotContain("bind-secret").And.NotContain("protected:");
        await _providers.Received(1).AddAsync(Arg.Is<AuthProvider>(p =>
            p.Type == AuthProviderType.Ldap && p.LdapBindPasswordCiphertext == "protected:bind-secret" && p.LdapMailAttribute == "mail"
            && p.LdapDisplayNameAttribute == "displayName" && p.LdapUniqueIdAttribute == "entryUUID" && p.LdapGroupAttribute == "memberOf"
            && p.Issuer == null && p.ClientId == null));
    }

    [Fact]
    public async Task Updating_without_a_bind_password_keeps_it_and_clearing_removes_it()
    {
        var existing = new AuthProvider { Type = AuthProviderType.Ldap, Slug = "directory", DisplayName = "Directory", LdapBindDn = "cn=svc,dc=x", LdapBindPasswordCiphertext = "protected:old" };
        _providers.GetByIdAsync(existing.Id).Returns(existing);

        await _manager.UpdateAsync(existing.Id, Request(r => r.LdapBindPassword = null), Origin);
        existing.LdapBindPasswordCiphertext.Should().Be("protected:old");

        await _manager.UpdateAsync(existing.Id, Request(r => r.LdapBindPassword = "new"), Origin);
        existing.LdapBindPasswordCiphertext.Should().Be("protected:new");

        await _manager.UpdateAsync(existing.Id, Request(r => { r.LdapBindPassword = null; r.ClearLdapBindPassword = true; }), Origin);
        existing.LdapBindPasswordCiphertext.Should().BeNull();
    }

    [Fact]
    public async Task Without_a_bind_account_no_bind_password_is_kept()
    {
        var result = await _manager.CreateAsync(Request(r => { r.LdapBindDn = null; r.LdapBindPassword = "ignored"; }), Origin);

        result.Outcome.Should().Be(AuthProviderSaveOutcome.Saved);
        result.Provider!.HasBindPassword.Should().BeFalse();
    }

    [Theory]
    [InlineData("ldap://ldap.example.org")]
    [InlineData("ldap.example.org:636")]
    [InlineData("ldap example")]
    [InlineData("ldap.example.org/path")]
    public async Task A_host_with_a_scheme_a_port_or_a_path_is_refused(string host)
    {
        var result = await _manager.CreateAsync(Request(r => r.LdapHost = host), Origin);

        result.Outcome.Should().Be(AuthProviderSaveOutcome.Invalid);
        result.Error.Should().Contain("host name or address");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public async Task A_port_out_of_range_is_refused(int port)
    {
        (await _manager.CreateAsync(Request(r => r.LdapPort = port), Origin)).Outcome.Should().Be(AuthProviderSaveOutcome.Invalid);
    }

    [Fact]
    public async Task An_unencrypted_connection_is_allowed_only_on_your_own_network()
    {
        (await _manager.CreateAsync(Request(r => { r.LdapHost = "192.168.0.10"; r.LdapSecurity = LdapSecurityMode.None; }), Origin)).Outcome.Should().Be(AuthProviderSaveOutcome.Saved);
        (await _manager.CreateAsync(Request(r => { r.Slug = "two"; r.LdapHost = "ldap.local"; r.LdapSecurity = LdapSecurityMode.None; }), Origin)).Outcome.Should().Be(AuthProviderSaveOutcome.Saved);

        var open = await _manager.CreateAsync(Request(r => { r.Slug = "three"; r.LdapHost = "ldap.example.com"; r.LdapSecurity = LdapSecurityMode.None; }), Origin);
        open.Outcome.Should().Be(AuthProviderSaveOutcome.Invalid);
        open.Error.Should().Contain("your own network");
    }

    [Theory]
    [InlineData("")]
    [InlineData("uid={username}")]
    [InlineData("(uid={username}")]
    [InlineData("(uid=alice)")]
    public async Task A_user_filter_that_is_malformed_or_lacks_the_placeholder_is_refused(string filter)
    {
        var result = await _manager.CreateAsync(Request(r => r.LdapUserFilter = filter), Origin);

        result.Outcome.Should().Be(AuthProviderSaveOutcome.Invalid);
    }

    [Fact]
    public async Task A_group_search_needs_both_its_base_and_its_filter_and_the_filter_a_placeholder()
    {
        (await _manager.CreateAsync(Request(r => r.LdapGroupBaseDn = "ou=groups,dc=x"), Origin)).Error.Should().Contain("both");
        (await _manager.CreateAsync(Request(r => r.LdapGroupFilter = "(member={dn})"), Origin)).Error.Should().Contain("both");
        (await _manager.CreateAsync(Request(r => { r.LdapGroupBaseDn = "ou=groups,dc=x"; r.LdapGroupFilter = "(objectClass=groupOfNames)"; }), Origin)).Error.Should().Contain("Group filter");

        var saved = await _manager.CreateAsync(Request(r => { r.LdapGroupBaseDn = "ou=groups,dc=x"; r.LdapGroupFilter = "(&(objectClass=groupOfNames)(member={dn}))"; }), Origin);
        saved.Outcome.Should().Be(AuthProviderSaveOutcome.Saved);
        saved.Provider!.LdapGroupFilter.Should().Be("(&(objectClass=groupOfNames)(member={dn}))");
    }

    [Theory]
    [InlineData("1mail")]
    [InlineData("ma il")]
    [InlineData("mail)(uid=*")]
    public async Task An_attribute_name_with_odd_characters_is_refused(string attribute)
    {
        var result = await _manager.CreateAsync(Request(r => r.LdapMailAttribute = attribute), Origin);

        result.Outcome.Should().Be(AuthProviderSaveOutcome.Invalid);
        result.Error.Should().Contain("attribute name");
    }

    [Fact]
    public async Task Attribute_names_fall_back_to_the_defaults_when_left_empty_and_may_carry_an_option()
    {
        var result = await _manager.CreateAsync(Request(r => { r.LdapUniqueIdAttribute = "objectGUID"; r.LdapMailAttribute = "mail;lang-en"; r.LdapDisplayNameAttribute = " "; }), Origin);

        result.Outcome.Should().Be(AuthProviderSaveOutcome.Saved);
        result.Provider!.LdapUniqueIdAttribute.Should().Be("objectGUID");
        result.Provider.LdapMailAttribute.Should().Be("mail;lang-en");
        result.Provider.LdapDisplayNameAttribute.Should().Be("displayName");
    }

    [Fact]
    public async Task A_certificate_authority_must_be_a_pem_certificate()
    {
        (await _manager.CreateAsync(Request(r => r.LdapCaCertificatePem = "not a certificate"), Origin)).Error.Should().Contain("PEM certificate");

        var pem = CertificatePem();
        var saved = await _manager.CreateAsync(Request(r => r.LdapCaCertificatePem = pem), Origin);
        saved.Outcome.Should().Be(AuthProviderSaveOutcome.Saved);
        saved.Provider!.LdapCaCertificatePem.Should().Be(pem);
    }

    [Fact]
    public async Task A_directory_needs_its_host_and_search_settings_before_it_is_turned_on_but_a_draft_is_kept()
    {
        var incomplete = Request(r => { r.LdapHost = null; r.LdapUserBaseDn = null; r.LdapUserFilter = null; });

        (await _manager.CreateAsync(incomplete, Origin)).Error.Should().Contain("before it can be turned on");

        incomplete.Enabled = false;
        (await _manager.CreateAsync(incomplete, Origin)).Outcome.Should().Be(AuthProviderSaveOutcome.Saved);
    }

    [Fact]
    public async Task The_policies_and_group_limits_apply_to_a_directory_too()
    {
        var result = await _manager.CreateAsync(Request(r =>
        {
            r.NewUserPolicy = AuthNewUserPolicy.ExistingOnly;
            r.LinkByEmail = AuthLinkByEmailPolicy.Automatic;
            r.AllowedEmailDomains = ["@Example.org"];
            r.RequiredGroup = "staff";
            r.AdminGroup = "admins";
        }), Origin);

        result.Provider!.NewUserPolicy.Should().Be(AuthNewUserPolicy.ExistingOnly);
        result.Provider.LinkByEmail.Should().Be(AuthLinkByEmailPolicy.Automatic);
        result.Provider.AllowedEmailDomains.Should().Equal("example.org");
        result.Provider.RequiredGroup.Should().Be("staff");
        result.Provider.AdminGroup.Should().Be("admins");
    }

    [Fact]
    public async Task A_failed_save_changes_nothing_on_the_provider()
    {
        var existing = new AuthProvider { Type = AuthProviderType.Ldap, Slug = "directory", DisplayName = "Directory", LdapHost = "keep.example.org", LdapUserFilter = "(uid={username})" };
        _providers.GetByIdAsync(existing.Id).Returns(existing);

        var result = await _manager.UpdateAsync(existing.Id, Request(r => { r.LdapHost = "new.example.org"; r.LdapUserFilter = "broken"; }), Origin);

        result.Outcome.Should().Be(AuthProviderSaveOutcome.Invalid);
        existing.LdapHost.Should().Be("keep.example.org");
        existing.LdapUserFilter.Should().Be("(uid={username})");
    }

    [Fact]
    public async Task The_public_list_has_no_button_for_a_directory_but_says_one_is_on()
    {
        var directory = new AuthProvider { Type = AuthProviderType.Ldap, Slug = "directory", DisplayName = "Directory", Enabled = true };
        var oidc = new AuthProvider { Type = AuthProviderType.Oidc, Slug = "authentik", DisplayName = "Authentik", Enabled = true };
        _providers.GetEnabledAsync().Returns([directory, oidc]);

        var list = await _manager.GetPublicAsync();

        list.DirectorySignInEnabled.Should().BeTrue();
        list.Providers.Select(p => p.Slug).Should().Equal("authentik");

        _providers.GetEnabledAsync().Returns([oidc]);
        (await _manager.GetPublicAsync()).DirectorySignInEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Password_sign_in_can_be_turned_off_with_only_a_directory_on_and_not_with_nothing_on()
    {
        _providers.GetEnabledAsync().Returns([]);
        (await _manager.UpdateSettingsAsync(new UpdateAuthSettingsRequest { PasswordSignInEnabled = false }, Origin)).Saved.Should().BeFalse();

        _providers.GetEnabledAsync().Returns([new AuthProvider { Type = AuthProviderType.Ldap, Slug = "directory", DisplayName = "Directory", Enabled = true }]);
        (await _manager.UpdateSettingsAsync(new UpdateAuthSettingsRequest { PasswordSignInEnabled = false }, Origin)).Saved.Should().BeTrue();
    }

    [Fact]
    public async Task The_type_of_a_provider_is_told_for_the_test_button()
    {
        var directory = new AuthProvider { Type = AuthProviderType.Ldap, Slug = "directory", DisplayName = "Directory" };
        _providers.GetByIdAsync(directory.Id).Returns(directory);

        (await _manager.GetTypeAsync(directory.Id)).Should().Be(AuthProviderType.Ldap);
        (await _manager.GetTypeAsync(Guid.NewGuid())).Should().BeNull();
    }
}
