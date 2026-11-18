using Vora.Application.Auth.External.Ldap;

namespace Vora.Application.Tests.Auth.Ldap;

public class LdapFilterTests
{
    [Theory]
    [InlineData("alice", "alice")]
    [InlineData("a*", "a\\2a")]
    [InlineData("(uid=*)", "\\28uid=\\2a\\29")]
    [InlineData("back\\slash", "back\\5cslash")]
    [InlineData("nul\0byte", "nul\\00byte")]
    [InlineData("zoë", "zoë")]
    public void The_characters_that_change_a_filter_are_escaped_and_the_rest_is_left_alone(string typed, string expected)
    {
        LdapFilter.Escape(typed).Should().Be(expected);
    }

    [Fact]
    public void A_name_built_to_widen_the_search_stays_one_literal_value()
    {
        var filter = LdapFilter.BuildUserFilter("(&(objectClass=person)(uid={username}))", "alice)(uid=*");

        filter.Should().Be("(&(objectClass=person)(uid=alice\\29\\28uid=\\2a))");
    }

    [Fact]
    public void A_lone_wildcard_cannot_match_everyone()
    {
        LdapFilter.BuildUserFilter("(uid={username})", "*").Should().Be("(uid=\\2a)");
    }

    [Fact]
    public void The_name_is_trimmed_before_it_goes_into_the_filter()
    {
        LdapFilter.BuildUserFilter("(uid={username})", "  alice  ").Should().Be("(uid=alice)");
    }

    [Fact]
    public void A_name_is_put_into_every_place_the_template_asks_for_it()
    {
        LdapFilter.BuildUserFilter("(|(uid={username})(mail={username}))", "a@b.c").Should().Be("(|(uid=a@b.c)(mail=a@b.c))");
    }

    [Fact]
    public void A_group_filter_gets_the_escaped_distinguished_name_and_name()
    {
        LdapFilter.BuildGroupFilter("(&(objectClass=groupOfNames)(member={dn}))", "uid=a(b),ou=people,dc=x", "alice")
            .Should().Be("(&(objectClass=groupOfNames)(member=uid=a\\28b\\29,ou=people,dc=x))");
        LdapFilter.BuildGroupFilter("(memberUid={username})", "uid=a,dc=x", "al*ce").Should().Be("(memberUid=al\\2ace)");
    }

    [Fact]
    public void The_test_search_asks_for_everyone()
    {
        LdapFilter.BuildProbeFilter("(&(objectClass=person)(uid={username}))").Should().Be("(&(objectClass=person)(uid=*))");
    }

    [Theory]
    [InlineData("alice", true)]
    [InlineData("alice@example.org", true)]
    [InlineData("  alice  ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("al\nice", false)]
    [InlineData("al\0ice", false)]
    [InlineData("al\tice", false)]
    public void A_name_that_is_empty_or_holds_a_control_character_is_not_searched_for(string? typed, bool valid)
    {
        LdapFilter.IsValidUsername(typed).Should().Be(valid);
    }

    [Fact]
    public void A_name_longer_than_the_limit_is_not_searched_for()
    {
        LdapFilter.IsValidUsername(new string('a', LdapFilter.MaxUsernameLength)).Should().BeTrue();
        LdapFilter.IsValidUsername(new string('a', LdapFilter.MaxUsernameLength + 1)).Should().BeFalse();
    }

    [Theory]
    [InlineData("(uid={username})")]
    [InlineData("(&(objectClass=person)(|(uid={username})(mail={username})))")]
    [InlineData("(&(objectClass=user)(|(sAMAccountName={username})(userPrincipalName={username})))")]
    public void A_user_filter_with_the_placeholder_and_balanced_parentheses_is_accepted(string template)
    {
        LdapFilter.ValidateTemplate(template, LdapFilter.UsernamePlaceholder).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("uid={username}")]
    [InlineData("(uid={username}")]
    [InlineData("uid={username})")]
    [InlineData("(uid={username}))")]
    [InlineData("(uid=alice)")]
    public void A_filter_that_is_empty_unbalanced_or_without_the_placeholder_is_refused(string? template)
    {
        LdapFilter.ValidateTemplate(template, LdapFilter.UsernamePlaceholder).Should().NotBeNull();
    }

    [Fact]
    public void A_filter_with_a_control_character_or_too_long_is_refused()
    {
        LdapFilter.ValidateTemplate("(uid={username})\n", LdapFilter.UsernamePlaceholder).Should().NotBeNull();
        LdapFilter.ValidateTemplate("(uid={username}" + new string('x', LdapFilter.MaxTemplateLength) + ")", LdapFilter.UsernamePlaceholder).Should().NotBeNull();
    }

    [Fact]
    public void An_escaped_parenthesis_does_not_count_toward_the_balance()
    {
        LdapFilter.ValidateTemplate("(cn=a\\28b{username})", LdapFilter.UsernamePlaceholder).Should().BeNull();
    }

    [Fact]
    public void A_group_filter_needs_the_distinguished_name_or_the_name()
    {
        LdapFilter.ValidateTemplate("(member={dn})", LdapFilter.DnPlaceholder, LdapFilter.UsernamePlaceholder).Should().BeNull();
        LdapFilter.ValidateTemplate("(memberUid={username})", LdapFilter.DnPlaceholder, LdapFilter.UsernamePlaceholder).Should().BeNull();
        LdapFilter.ValidateTemplate("(objectClass=groupOfNames)", LdapFilter.DnPlaceholder, LdapFilter.UsernamePlaceholder).Should().NotBeNull();
    }
}
