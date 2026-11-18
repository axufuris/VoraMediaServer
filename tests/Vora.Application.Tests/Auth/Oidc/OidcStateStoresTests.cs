using Vora.Application.Auth.External;
using Vora.Application.Auth.External.Oidc;

namespace Vora.Application.Tests.Auth.Oidc;

public class OidcStateStoresTests
{
    private readonly ManualClock _clock = new();

    private static OidcLoginState LoginState() => new(Guid.NewGuid(), "nonce", "verifier", "https://vora.example.com/cb", "/movies", "invite", null);

    private static ExternalPending Pending(int failures = 0) =>
        new(Guid.NewGuid(), "Authentik", new ExternalClaims("subject-1", "sam@example.com", true, "Sam", []), null, "/", ExternalSignInResult.NeedsCreateOrLink(true, ExternalSignInRefusal.None, null, false), failures);

    [Fact]
    public void A_login_state_is_returned_once()
    {
        var store = new OidcLoginStateStore(_clock);
        var state = LoginState();
        var key = store.Create(state);

        store.Take(key).Should().Be(state);
        store.Take(key).Should().BeNull();
    }

    [Fact]
    public void A_login_state_expires_after_ten_minutes()
    {
        var store = new OidcLoginStateStore(_clock);
        var key = store.Create(LoginState());
        _clock.Now = _clock.Now.Add(OidcLoginStateStore.Lifetime).AddSeconds(1);

        store.Take(key).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("guess")]
    public void A_guessed_state_gives_nothing(string guess)
    {
        var store = new OidcLoginStateStore(_clock);
        store.Create(LoginState());

        store.Take(guess).Should().BeNull();
    }

    [Fact]
    public void States_are_long_and_never_repeat()
    {
        var store = new OidcLoginStateStore(_clock);

        var keys = Enumerable.Range(0, 40).Select(_ => store.Create(LoginState())).ToList();

        keys.Distinct().Should().HaveCount(40);
        keys.Should().OnlyContain(key => key.Length >= 40);
    }

    [Fact]
    public void A_pending_sign_in_can_be_read_updated_and_removed()
    {
        var store = new ExternalPendingStore(_clock);
        var handle = store.Create(Pending());

        store.Find(handle).Should().NotBeNull();
        store.Update(handle, Pending(failures: 2));
        store.Find(handle)!.Failures.Should().Be(2);
        store.Remove(handle);
        store.Find(handle).Should().BeNull();
    }

    [Fact]
    public void A_pending_sign_in_expires_after_ten_minutes()
    {
        var store = new ExternalPendingStore(_clock);
        var handle = store.Create(Pending());
        _clock.Now = _clock.Now.Add(ExternalPendingStore.Lifetime).AddSeconds(1);

        store.Find(handle).Should().BeNull();
    }

    [Fact]
    public void An_unknown_pending_handle_changes_nothing()
    {
        var store = new ExternalPendingStore(_clock);
        store.Create(Pending());

        store.Find("guess").Should().BeNull();
        store.Update("guess", Pending());
        store.Find("guess").Should().BeNull();
        store.Remove("guess");
    }
}
