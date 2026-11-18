using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Auth.External.Ldap;
using Vora.Domain.Enums;
using Vora.Infrastructure.Auth;

namespace Vora.Infrastructure.Tests.Ldap;

public class NovellLdapDirectoryTests
{
    private const string UserFilter = "(&(objectClass=inetOrgPerson)(|(uid={username})(mail={username})))";
    private const string GroupFilter = "(&(objectClass=groupOfNames)(member={dn}))";
    private const string SkipReason = "Set VORA_TEST_LDAP (host:plain-port:ldaps-port) and VORA_TEST_LDAP_CA (a PEM file) to run against a real directory; see tests/Vora.Infrastructure.Tests/Ldap/README.md.";

    private static readonly string? Endpoint = Environment.GetEnvironmentVariable("VORA_TEST_LDAP");
    private static readonly string? CaPath = Environment.GetEnvironmentVariable("VORA_TEST_LDAP_CA");

    private readonly NovellLdapDirectory _directory = new(NullLogger<NovellLdapDirectory>.Instance);

    private static bool Configured => !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(CaPath) && File.Exists(CaPath);

    private static (string Host, int PlainPort, int SecurePort) Parts()
    {
        var parts = Endpoint!.Split(':');
        return (parts[0], int.Parse(parts[1]), int.Parse(parts[2]));
    }

    private static LdapConnectionSettings Settings(Func<LdapConnectionSettings, LdapConnectionSettings>? change = null)
    {
        var (host, _, securePort) = Parts();
        var settings = new LdapConnectionSettings(
            host,
            securePort,
            LdapSecurityMode.Ldaps,
            File.ReadAllText(CaPath!),
            "cn=admin,dc=example,dc=org",
            "adminpw",
            "ou=people,dc=example,dc=org",
            UserFilter,
            "mail",
            "displayName",
            "entryUUID",
            "memberOf",
            null,
            null);
        return change is null ? settings : change(settings);
    }

    private Task<LdapAuthResult> SignInAsync(string name, string password, Func<LdapConnectionSettings, LdapConnectionSettings>? change = null) =>
        _directory.AuthenticateAsync(Settings(change), name, password, TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_person_signs_in_over_ldaps_with_the_certificate_authority_and_their_entry_is_read()
    {
        Assert.SkipUnless(Configured, SkipReason);

        var result = await SignInAsync("alice", "alicepw");

        result.Status.Should().Be(LdapAuthStatus.Authenticated);
        result.User!.Dn.Should().Be("uid=alice,ou=people,dc=example,dc=org");
        result.User.Mail.Should().Be("alice@example.org");
        result.User.DisplayName.Should().Be("Alice Example");
        Guid.TryParse(result.User.UniqueId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task A_person_may_sign_in_with_their_mail_when_the_filter_allows_it()
    {
        Assert.SkipUnless(Configured, SkipReason);

        var result = await SignInAsync("bob@example.org", "bobpw");

        result.Status.Should().Be(LdapAuthStatus.Authenticated);
        result.User!.Dn.Should().Be("uid=bob,ou=people,dc=example,dc=org");
    }

    [Fact]
    public async Task The_name_is_not_case_sensitive_the_way_the_directory_matches_it()
    {
        Assert.SkipUnless(Configured, SkipReason);

        (await SignInAsync("ALICE", "alicepw")).Status.Should().Be(LdapAuthStatus.Authenticated);
    }

    [Theory]
    [InlineData("alice", "wrong")]
    [InlineData("alice", "alicepw ")]
    [InlineData("nobody", "alicepw")]
    [InlineData("alice", "bobpw")]
    public async Task A_wrong_password_or_an_unknown_name_is_invalid_credentials(string name, string password)
    {
        Assert.SkipUnless(Configured, SkipReason);

        var result = await SignInAsync(name, password);

        result.Status.Should().Be(LdapAuthStatus.InvalidCredentials);
        result.User.Should().BeNull();
    }

    [Fact]
    public async Task An_empty_password_is_never_accepted_even_though_an_unauthenticated_bind_would_succeed()
    {
        Assert.SkipUnless(Configured, SkipReason);

        (await SignInAsync("alice", string.Empty)).Status.Should().Be(LdapAuthStatus.InvalidCredentials);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("alice*")]
    [InlineData("alice)(uid=bob")]
    [InlineData("alice)(|(uid=*")]
    [InlineData("*)(uid=*")]
    public async Task A_name_built_to_widen_the_search_matches_nobody(string name)
    {
        Assert.SkipUnless(Configured, SkipReason);

        var result = await SignInAsync(name, "alicepw");

        result.Status.Should().Be(LdapAuthStatus.InvalidCredentials);
    }

    [Fact]
    public async Task A_name_that_matches_two_entries_signs_nobody_in()
    {
        Assert.SkipUnless(Configured, SkipReason);

        (await SignInAsync("dup@example.org", "dup1pw")).Status.Should().Be(LdapAuthStatus.InvalidCredentials);
        (await SignInAsync("dup@example.org", "dup2pw")).Status.Should().Be(LdapAuthStatus.InvalidCredentials);
    }

    [Fact]
    public async Task An_entry_without_mail_still_authenticates_and_comes_back_with_no_mail_for_the_caller_to_refuse()
    {
        Assert.SkipUnless(Configured, SkipReason);

        var result = await SignInAsync("carol", "carolpw");

        result.Status.Should().Be(LdapAuthStatus.Authenticated);
        result.User!.Mail.Should().BeNull();
    }

    [Fact]
    public async Task Groups_come_from_a_group_search_by_the_users_distinguished_name()
    {
        Assert.SkipUnless(Configured, SkipReason);

        var result = await SignInAsync("alice", "alicepw", s => s with { GroupBaseDn = "ou=groups,dc=example,dc=org", GroupFilter = GroupFilter });

        result.User!.Groups.Should().Contain("cn=admins,ou=groups,dc=example,dc=org").And.Contain("admins").And.Contain("viewers");
        (await SignInAsync("bob", "bobpw", s => s with { GroupBaseDn = "ou=groups,dc=example,dc=org", GroupFilter = GroupFilter })).User!.Groups
            .Should().Contain("viewers").And.NotContain("admins");
    }

    [Fact]
    public async Task A_person_signs_in_over_starttls_on_the_plain_port()
    {
        Assert.SkipUnless(Configured, SkipReason);
        var (_, plainPort, _) = Parts();

        var result = await SignInAsync("alice", "alicepw", s => s with { Port = plainPort, Security = LdapSecurityMode.StartTls });

        result.Status.Should().Be(LdapAuthStatus.Authenticated);
    }

    [Fact]
    public async Task A_person_signs_in_without_encryption_when_the_admin_chose_that()
    {
        Assert.SkipUnless(Configured, SkipReason);
        var (_, plainPort, _) = Parts();

        var result = await SignInAsync("alice", "alicepw", s => s with { Port = plainPort, Security = LdapSecurityMode.None, CaCertificatePem = null });

        result.Status.Should().Be(LdapAuthStatus.Authenticated);
    }

    [Fact]
    public async Task Without_the_private_authority_the_directorys_certificate_is_not_trusted()
    {
        Assert.SkipUnless(Configured, SkipReason);

        var result = await SignInAsync("alice", "alicepw", s => s with { CaCertificatePem = null });

        result.Status.Should().Be(LdapAuthStatus.Unreachable);
        result.Detail.Should().NotContain("alicepw").And.NotContain("alice");
    }

    [Fact]
    public async Task A_host_the_certificate_was_not_made_for_is_refused()
    {
        Assert.SkipUnless(Configured, SkipReason);

        var result = await SignInAsync("alice", "alicepw", s => s with { Host = "127.0.0.2" });

        result.Status.Should().Be(LdapAuthStatus.Unreachable);
    }

    [Fact]
    public async Task A_bind_account_the_directory_refuses_is_unreachable_not_invalid_credentials()
    {
        Assert.SkipUnless(Configured, SkipReason);

        var result = await SignInAsync("alice", "alicepw", s => s with { BindPassword = "not-the-password" });

        result.Status.Should().Be(LdapAuthStatus.Unreachable);
        result.Detail.Should().Contain("bind account").And.NotContain("not-the-password");
    }

    [Fact]
    public async Task A_port_nothing_listens_on_is_unreachable_and_fast()
    {
        Assert.SkipUnless(Configured, SkipReason);

        var result = await SignInAsync("alice", "alicepw", s => s with { Port = 1 });

        result.Status.Should().Be(LdapAuthStatus.Unreachable);
        result.Detail.Should().Contain("Could not connect");
    }

    [Fact]
    public async Task A_search_base_that_does_not_exist_is_reported_to_the_admin_and_not_as_a_wrong_password()
    {
        Assert.SkipUnless(Configured, SkipReason);

        var result = await SignInAsync("alice", "alicepw", s => s with { UserBaseDn = "ou=nobody,dc=example,dc=org" });

        result.Status.Should().Be(LdapAuthStatus.Unreachable);
        result.Detail.Should().Contain("does not exist");
    }

    [Fact]
    public async Task The_test_search_counts_the_people_and_how_many_have_mail_and_a_unique_id()
    {
        Assert.SkipUnless(Configured, SkipReason);

        var sample = await _directory.SampleAsync(Settings(), TestContext.Current.CancellationToken);

        sample.Connected.Should().BeTrue();
        sample.ServiceBound.Should().BeTrue();
        sample.EntriesFound.Should().Be(5);
        sample.WithMail.Should().Be(4);
        sample.WithoutMail.Should().Be(1);
        sample.WithUniqueId.Should().Be(5);
        sample.SearchLimited.Should().BeFalse();
        sample.SampleDns.Should().HaveCount(3).And.OnlyContain(dn => dn.EndsWith("ou=people,dc=example,dc=org", StringComparison.Ordinal));
        sample.Detail.Should().BeNull();
    }

    [Fact]
    public async Task The_test_search_says_which_step_failed()
    {
        Assert.SkipUnless(Configured, SkipReason);

        var refused = await _directory.SampleAsync(Settings(s => s with { BindPassword = "wrong" }), TestContext.Current.CancellationToken);
        refused.Connected.Should().BeTrue();
        refused.ServiceBound.Should().BeFalse();
        refused.Detail.Should().Contain("bind account");

        var down = await _directory.SampleAsync(Settings(s => s with { Port = 1 }), TestContext.Current.CancellationToken);
        down.Connected.Should().BeFalse();
        down.Detail.Should().Contain("Could not connect");

        var empty = await _directory.SampleAsync(Settings(s => s with { UserFilter = "(&(objectClass=inetOrgPerson)(uid={username})(mail=nobody@nowhere))" }), TestContext.Current.CancellationToken);
        empty.ServiceBound.Should().BeTrue();
        empty.EntriesFound.Should().Be(0);
    }

    [Fact]
    public async Task An_empty_name_or_password_does_not_touch_the_network()
    {
        var unreachable = new LdapConnectionSettings("127.0.0.1", 1, LdapSecurityMode.None, null, null, null, "dc=x", "(uid={username})", "mail", "cn", "entryUUID", "memberOf", null, null);

        (await _directory.AuthenticateAsync(unreachable, " ", "pw", TestContext.Current.CancellationToken)).Status.Should().Be(LdapAuthStatus.InvalidCredentials);
        (await _directory.AuthenticateAsync(unreachable, "alice", string.Empty, TestContext.Current.CancellationToken)).Status.Should().Be(LdapAuthStatus.InvalidCredentials);
    }
}
