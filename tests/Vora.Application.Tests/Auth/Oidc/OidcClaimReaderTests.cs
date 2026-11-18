using System.Text.Json;
using Vora.Application.Auth.External.Oidc;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Tests.Auth.Oidc;

public class OidcClaimReaderTests
{
    private readonly AuthProvider _provider = OidcTestKit.Provider();

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static Dictionary<string, object> Claims(params (string Key, object Value)[] pairs) =>
        new[] { ("sub", (object)"subject-1") }.Concat(pairs).ToDictionary(pair => pair.Item1, pair => pair.Item2);

    [Fact]
    public void The_standard_claims_are_read_into_a_sign_in()
    {
        var claims = OidcClaimReader.Read(_provider, Claims(("email", "Sam@Example.com "), ("email_verified", true), ("name", " Sam "), ("groups", new List<object> { "family", "admins" })), null)!;

        claims.Subject.Should().Be("subject-1");
        claims.Email.Should().Be("Sam@Example.com");
        claims.EmailVerified.Should().BeTrue();
        claims.DisplayName.Should().Be("Sam");
        claims.Groups.Should().BeEquivalentTo("family", "admins");
    }

    [Fact]
    public void Without_a_subject_there_is_no_sign_in()
    {
        OidcClaimReader.Read(_provider, new Dictionary<string, object> { ["email"] = "sam@example.com" }, null).Should().BeNull();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData(false, false)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    [InlineData(1, false)]
    public void Email_verified_is_true_only_when_the_provider_says_so(object value, bool expected)
    {
        OidcClaimReader.Read(_provider, Claims(("email", "sam@example.com"), ("email_verified", value)), null)!.EmailVerified.Should().Be(expected);
    }

    [Fact]
    public void An_address_is_never_verified_when_the_provider_sent_none()
    {
        OidcClaimReader.Read(_provider, Claims(("email_verified", true)), null)!.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public void Claims_that_arrive_as_JSON_are_read_the_same_way()
    {
        var claims = OidcClaimReader.Read(_provider, Claims(("email", Json("\"sam@example.com\"")), ("email_verified", Json("true")), ("groups", Json("[\"a\",\"b\",\"A\"]"))), null)!;

        claims.Email.Should().Be("sam@example.com");
        claims.EmailVerified.Should().BeTrue();
        claims.Groups.Should().BeEquivalentTo("a", "b");
    }

    [Theory]
    [InlineData("family admins", new[] { "family", "admins" })]
    [InlineData("family,admins", new[] { "family", "admins" })]
    [InlineData("  ", new string[0])]
    public void Groups_sent_as_one_string_are_split(string value, string[] expected)
    {
        OidcClaimReader.Read(_provider, Claims(("groups", value)), null)!.Groups.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void The_claim_names_come_from_the_provider_settings()
    {
        _provider.EmailClaim = "mail";
        _provider.NameClaim = "display";
        _provider.GroupsClaim = "roles";

        var claims = OidcClaimReader.Read(_provider, Claims(("mail", "sam@example.com"), ("display", "Sam S"), ("roles", new List<object> { "x" }), ("email", "other@example.com")), null)!;

        claims.Email.Should().Be("sam@example.com");
        claims.DisplayName.Should().Be("Sam S");
        claims.Groups.Should().Equal("x");
    }

    [Fact]
    public void A_missing_name_falls_back_to_the_preferred_username()
    {
        OidcClaimReader.Read(_provider, Claims(("preferred_username", "sam")), null)!.DisplayName.Should().Be("sam");
    }

    [Fact]
    public void User_info_fills_in_what_the_id_token_lacked_only_for_the_same_subject()
    {
        var info = Json("{\"sub\":\"subject-1\",\"email\":\"sam@example.com\",\"email_verified\":true,\"groups\":[\"vora\"]}");
        var someoneElse = Json("{\"sub\":\"subject-2\",\"email\":\"other@example.com\",\"email_verified\":true}");

        var merged = OidcClaimReader.Read(_provider, Claims(), info)!;
        var refused = OidcClaimReader.Read(_provider, Claims(), someoneElse)!;

        merged.Email.Should().Be("sam@example.com");
        merged.EmailVerified.Should().BeTrue();
        merged.Groups.Should().Equal("vora");
        refused.Email.Should().BeNull();
    }

    [Fact]
    public void The_id_token_wins_over_user_info()
    {
        var info = Json("{\"sub\":\"subject-1\",\"email\":\"info@example.com\"}");

        OidcClaimReader.Read(_provider, Claims(("email", "token@example.com")), info)!.Email.Should().Be("token@example.com");
    }

    [Fact]
    public void User_info_is_asked_for_only_when_something_needed_is_missing()
    {
        var complete = Claims(("email", "sam@example.com"));
        OidcClaimReader.NeedsUserInfo(_provider, complete).Should().BeFalse();
        OidcClaimReader.NeedsUserInfo(_provider, Claims()).Should().BeTrue();

        _provider.RequiredGroup = "vora";
        OidcClaimReader.NeedsUserInfo(_provider, complete).Should().BeTrue();
        OidcClaimReader.NeedsUserInfo(_provider, Claims(("email", "sam@example.com"), ("groups", new List<object> { "vora" }))).Should().BeFalse();
    }
}
