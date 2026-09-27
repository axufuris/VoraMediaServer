using Vora.Application.Users;

namespace Vora.Application.Tests.Auth;

public class ProfilePinTests
{
    [Fact]
    public void A_profile_without_a_pin_accepts_anything()
    {
        ProfilePin.IsSet(null).Should().BeFalse();
        ProfilePin.Verify(null, null).Should().BeTrue();
        ProfilePin.Verify("", "9999").Should().BeTrue();
    }

    [Fact]
    public void A_bcrypt_pin_matches_only_itself()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("1234");

        ProfilePin.Verify(hash, "1234").Should().BeTrue();
        ProfilePin.Verify(hash, "4321").Should().BeFalse();
    }

    [Fact]
    public void A_legacy_sha256_pin_still_matches()
    {
        var legacy = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("1234")));

        ProfilePin.Verify(legacy, "1234").Should().BeTrue();
        ProfilePin.Verify(legacy, "0000").Should().BeFalse();
    }

    [Fact]
    public void A_protected_profile_rejects_a_missing_pin()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("1234");

        ProfilePin.Verify(hash, null).Should().BeFalse();
        ProfilePin.Verify(hash, "").Should().BeFalse();
    }
}
