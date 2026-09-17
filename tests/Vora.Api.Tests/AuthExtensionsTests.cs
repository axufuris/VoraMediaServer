using System.Security.Claims;
using Vora.Api.Extensions;

namespace Vora.Api.Tests;

public class AuthExtensionsTests
{
    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)));
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public void GetProfileId_returns_sub_when_accountId_claim_is_present_and_valid()
    {
        var profile = Guid.NewGuid();
        var account = Guid.NewGuid();
        var user = Principal(
            (ClaimTypes.NameIdentifier, profile.ToString()),
            ("accountId", account.ToString()));

        user.GetProfileId().Should().Be(profile);
    }

    [Fact]
    public void GetProfileId_returns_legacy_sub_when_no_accountId_claim()
    {
        var legacy = Guid.NewGuid();
        var user = Principal(("sub", legacy.ToString()));

        user.GetProfileId().Should().Be(legacy);
    }

    [Fact]
    public void GetProfileId_returns_null_when_no_claims()
    {
        Principal().GetProfileId().Should().BeNull();
    }

    [Fact]
    public void GetProfileId_returns_null_when_sub_is_not_a_guid()
    {
        var user = Principal((ClaimTypes.NameIdentifier, "not-a-guid"));

        user.GetProfileId().Should().BeNull();
    }

    [Fact]
    public void GetAccountId_prefers_accountId_claim_over_sub()
    {
        var profile = Guid.NewGuid();
        var account = Guid.NewGuid();
        var user = Principal(
            (ClaimTypes.NameIdentifier, profile.ToString()),
            ("accountId", account.ToString()));

        user.GetAccountId().Should().Be(account);
    }

    [Fact]
    public void GetAccountId_falls_back_to_sub_when_accountId_absent()
    {
        var legacy = Guid.NewGuid();
        var user = Principal(("sub", legacy.ToString()));

        user.GetAccountId().Should().Be(legacy);
    }

    [Fact]
    public void HasAllLibraryAccess_true_when_claim_says_true()
    {
        var user = Principal(("hasAllLibraryAccess", "true"));

        user.HasAllLibraryAccess().Should().BeTrue();
    }

    [Fact]
    public void HasAllLibraryAccess_false_when_claim_missing()
    {
        Principal().HasAllLibraryAccess().Should().BeFalse();
    }

    [Fact]
    public void HasAllLibraryAccess_false_when_claim_is_garbage()
    {
        Principal(("hasAllLibraryAccess", "yes-please")).HasAllLibraryAccess().Should().BeFalse();
    }

    [Fact]
    public void GetAllowedLibraryIds_returns_valid_guids_only()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var user = Principal(
            ("allowedLibrary", a.ToString()),
            ("allowedLibrary", "not-a-guid"),
            ("allowedLibrary", b.ToString()));

        user.GetAllowedLibraryIds().Should().BeEquivalentTo(new[] { a, b });
    }

    [Fact]
    public void GetAllowedLibraryIds_empty_when_no_claims()
    {
        Principal().GetAllowedLibraryIds().Should().BeEmpty();
    }

    [Fact]
    public void GetAllowedMovieRatings_returns_all_claim_values()
    {
        var user = Principal(
            ("allowedMovieRating", "G"),
            ("allowedMovieRating", "PG"),
            ("allowedMovieRating", "PG-13"));

        user.GetAllowedMovieRatings().Should().BeEquivalentTo(new[] { "G", "PG", "PG-13" });
    }

    [Fact]
    public void IsAdmin_true_only_when_claim_says_true()
    {
        Principal(("isAdmin", "true")).IsAdmin().Should().BeTrue();
        Principal(("isAdmin", "false")).IsAdmin().Should().BeFalse();
        Principal().IsAdmin().Should().BeFalse();
    }

    [Fact]
    public void CanTimeshiftIptv_true_for_admins_regardless_of_claim()
    {
        var user = Principal(("isAdmin", "true"), ("canTimeshiftIptv", "false"));

        user.CanTimeshiftIptv().Should().BeTrue();
    }

    [Fact]
    public void CanTimeshiftIptv_falls_through_to_claim_for_non_admins()
    {
        Principal(("canTimeshiftIptv", "true")).CanTimeshiftIptv().Should().BeTrue();
        Principal(("canTimeshiftIptv", "false")).CanTimeshiftIptv().Should().BeFalse();
        Principal().CanTimeshiftIptv().Should().BeFalse();
    }

    [Fact]
    public void CanRecordLiveTv_true_for_admins_regardless_of_claim()
    {
        var user = Principal(("isAdmin", "true"));

        user.CanRecordLiveTv().Should().BeTrue();
    }

    [Fact]
    public void BlockUnratedContent_reads_claim()
    {
        Principal(("blockUnrated", "true")).BlockUnratedContent().Should().BeTrue();
        Principal(("blockUnrated", "false")).BlockUnratedContent().Should().BeFalse();
        Principal().BlockUnratedContent().Should().BeFalse();
    }
}
