using Vora.Application.Auth.External.Ldap;

namespace Vora.Application.Tests.Auth.Ldap;

public class LdapHostRulesTests
{
    [Theory]
    [InlineData("ldap.example.com", true)]
    [InlineData("dc1", true)]
    [InlineData("192.168.0.10", true)]
    [InlineData("::1", true)]
    [InlineData("fd00::10", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("ldap://ldap.example.com", false)]
    [InlineData("ldap.example.com:636", false)]
    [InlineData("ldap.example.com/path", false)]
    [InlineData("ldap example", false)]
    [InlineData("-bad.example.com", false)]
    public void A_host_is_a_name_or_an_address_by_itself(string host, bool valid)
    {
        LdapHostRules.IsValidHost(host).Should().Be(valid);
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("192.168.0.10", true)]
    [InlineData("169.254.1.1", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("fd12:3456::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("::ffff:192.168.0.10", true)]
    [InlineData("dc1", true)]
    [InlineData("nas.local", true)]
    [InlineData("ldap.lan", true)]
    [InlineData("directory.home.arpa", true)]
    [InlineData("ldap.example.internal", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("172.15.0.1", false)]
    [InlineData("100.128.0.1", false)]
    [InlineData("2001:4860:4860::8888", false)]
    [InlineData("ldap.example.com", false)]
    [InlineData("corp.example.org", false)]
    public void Only_a_host_on_your_own_network_may_be_reached_without_encryption(string host, bool onOwnNetwork)
    {
        LdapHostRules.IsPrivateNetworkHost(host).Should().Be(onOwnNetwork);
    }
}

public class LdapClaimMapperTests
{
    private static LdapUserEntry Entry(string? mail = "alice@example.org", string? uniqueId = "1234-abcd", string? name = "Alice Example", params string[] groups) =>
        new("uid=Alice,ou=People,dc=example,dc=org", uniqueId, mail, name, groups);

    [Fact]
    public void An_entry_with_a_mail_becomes_verified_claims_tied_to_its_unique_id()
    {
        var mapped = LdapClaimMapper.Map(Entry());

        mapped.MailMissing.Should().BeFalse();
        mapped.Claims.Should().NotBeNull();
        mapped.Claims!.Subject.Should().Be("1234-abcd");
        mapped.Claims.Email.Should().Be("alice@example.org");
        mapped.Claims.EmailVerified.Should().BeTrue();
        mapped.Claims.DisplayName.Should().Be("Alice Example");
    }

    [Fact]
    public void Without_a_unique_id_the_subject_is_the_lower_cased_distinguished_name()
    {
        LdapClaimMapper.Map(Entry(uniqueId: null)).Claims!.Subject.Should().Be("uid=alice,ou=people,dc=example,dc=org");
        LdapClaimMapper.Map(Entry(uniqueId: "  ")).Claims!.Subject.Should().Be("uid=alice,ou=people,dc=example,dc=org");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    public void An_entry_without_a_usable_mail_is_not_turned_into_claims(string? mail)
    {
        var mapped = LdapClaimMapper.Map(Entry(mail: mail));

        mapped.MailMissing.Should().BeTrue();
        mapped.Claims.Should().BeNull();
    }

    [Fact]
    public void A_blank_display_name_is_left_out()
    {
        LdapClaimMapper.Map(Entry(name: "  ")).Claims!.DisplayName.Should().BeNull();
    }

    [Fact]
    public void A_group_counts_by_its_distinguished_name_and_by_its_name()
    {
        var mapped = LdapClaimMapper.Map(Entry(groups: ["cn=Admins,ou=Groups,dc=example,dc=org", "viewers"]));

        mapped.Claims!.Groups.Should().BeEquivalentTo("cn=Admins,ou=Groups,dc=example,dc=org", "Admins", "viewers");
    }

    [Fact]
    public void Groups_are_listed_once_whatever_the_case()
    {
        LdapClaimMapper.Groups(["Admins", "admins", "cn=admins,dc=x", "  "]).Should().BeEquivalentTo("Admins", "cn=admins,dc=x");
    }

    [Theory]
    [InlineData("cn=Admins,ou=Groups,dc=x", "Admins")]
    [InlineData("CN=Domain Users,CN=Users,DC=corp,DC=example", "Domain Users")]
    [InlineData("cn=Smith\\, Jo,ou=g,dc=x", "Smith, Jo")]
    [InlineData("cn=a+uid=b,dc=x", "a")]
    [InlineData("plain", null)]
    [InlineData("=x", null)]
    public void The_name_of_a_group_is_its_first_relative_name(string dn, string? expected)
    {
        LdapClaimMapper.FirstRdnValue(dn).Should().Be(expected);
    }
}
