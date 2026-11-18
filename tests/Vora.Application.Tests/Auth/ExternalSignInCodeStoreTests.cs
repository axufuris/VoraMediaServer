using Vora.Application.Auth.External;

namespace Vora.Application.Tests.Auth;

public class ExternalSignInCodeStoreTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly ManualClock _clock = new();
    private readonly ExternalSignInCodeStore _store;

    public ExternalSignInCodeStoreTests()
    {
        _store = new ExternalSignInCodeStore(_clock);
    }

    [Fact]
    public void A_code_gives_back_who_it_was_issued_for()
    {
        var user = Guid.NewGuid();
        var provider = Guid.NewGuid();

        var grant = _store.Redeem(_store.Issue(user, provider));

        grant.Should().Be(new ExternalSignInGrant(user, provider));
    }

    [Fact]
    public void A_code_works_once()
    {
        var code = _store.Issue(Guid.NewGuid(), Guid.NewGuid());

        _store.Redeem(code).Should().NotBeNull();
        _store.Redeem(code).Should().BeNull();
    }

    [Fact]
    public void A_code_expires_after_a_minute()
    {
        var code = _store.Issue(Guid.NewGuid(), Guid.NewGuid());
        _clock.Now = _clock.Now.Add(ExternalSignInCodeStore.Lifetime).AddSeconds(1);

        _store.Redeem(code).Should().BeNull();
    }

    [Fact]
    public void A_code_just_inside_its_lifetime_still_works()
    {
        var code = _store.Issue(Guid.NewGuid(), Guid.NewGuid());
        _clock.Now = _clock.Now.Add(ExternalSignInCodeStore.Lifetime).AddSeconds(-1);

        _store.Redeem(code).Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-code")]
    public void A_guess_gives_nothing(string guess)
    {
        _store.Issue(Guid.NewGuid(), Guid.NewGuid());

        _store.Redeem(guess).Should().BeNull();
    }

    [Fact]
    public void Codes_are_long_unique_and_url_safe()
    {
        var codes = Enumerable.Range(0, 50).Select(_ => _store.Issue(Guid.NewGuid(), Guid.NewGuid())).ToList();

        codes.Distinct().Should().HaveCount(50);
        codes.Should().OnlyContain(code => code.Length >= 40 && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_'));
    }

    [Fact]
    public void Expired_codes_are_dropped_when_a_new_one_is_issued()
    {
        var old = _store.Issue(Guid.NewGuid(), Guid.NewGuid());
        _clock.Now = _clock.Now.AddMinutes(5);

        _store.Issue(Guid.NewGuid(), Guid.NewGuid());

        _store.Redeem(old).Should().BeNull();
    }
}
