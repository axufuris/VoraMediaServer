using Vora.Application.Auth.Pairing;

namespace Vora.Application.Tests.Auth;

public class DevicePairingStoreTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan by) => Now += by;
    }

    private static readonly DevicePairingDevice Tv = new("tv-1", "Vora Roku", "Living Room", "TV", "Roku OS", "192.168.0.50");

    private readonly ManualClock _clock = new();
    private readonly DevicePairingStore _store;

    public DevicePairingStoreTests()
    {
        _store = new DevicePairingStore(_clock);
    }

    [Fact]
    public void A_user_code_is_eight_characters_from_an_alphabet_with_nothing_easy_to_confuse()
    {
        var codes = Enumerable.Range(0, 300).Select(_ => DevicePairingCode.Generate()).ToList();

        codes.Should().OnlyContain(code => code.Length == 8 && code.All(c => DevicePairingCode.Alphabet.Contains(c)));
        DevicePairingCode.Alphabet.Should().NotContainAny("0", "O", "1", "I", "L", "A", "E", "U");
        codes.Distinct().Should().HaveCountGreaterThan(290);
    }

    [Theory]
    [InlineData("BCDF-GHJK", "BCDFGHJK")]
    [InlineData("bcdf ghjk", "BCDFGHJK")]
    [InlineData("  bcdfghjk ", "BCDFGHJK")]
    public void A_typed_code_is_read_whatever_its_case_or_dash(string typed, string expected)
    {
        DevicePairingCode.Normalize(typed).Should().Be(expected);
        DevicePairingCode.Format(expected).Should().Be("BCDF-GHJK");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("BCDFGHJ")]
    [InlineData("BCDFGHJKM")]
    [InlineData("BCDF0HJK")]
    [InlineData("BCDFOHJK")]
    [InlineData("BCDFAHJK")]
    public void A_code_of_the_wrong_length_or_with_a_character_outside_the_alphabet_is_not_a_code(string? typed)
    {
        DevicePairingCode.Normalize(typed).Should().BeNull();
    }

    [Fact]
    public void A_started_pairing_waits_for_an_answer_and_knows_which_device_asked()
    {
        var issued = _store.Create(Tv);

        var pending = _store.FindPending(issued.UserCode);

        pending.Should().NotBeNull();
        pending!.Device.Should().Be(Tv);
        issued.IntervalSeconds.Should().Be(5);
        issued.ExpiresAt.Should().Be(_clock.Now.AddMinutes(10));
        issued.DeviceCode.Length.Should().BeGreaterThan(40);
    }

    [Fact]
    public void Two_pairings_never_share_a_code()
    {
        var first = _store.Create(Tv);
        var second = _store.Create(Tv);

        second.UserCode.Should().NotBe(first.UserCode);
        second.DeviceCode.Should().NotBe(first.DeviceCode);
    }

    [Fact]
    public void A_pairing_that_nobody_answers_expires_after_ten_minutes()
    {
        var issued = _store.Create(Tv);

        _clock.Advance(TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(1)));

        _store.FindPending(issued.UserCode).Should().BeNull();
        _store.Poll(issued.DeviceCode).Status.Should().Be(DevicePairingPollStatus.Expired);
        _store.Decide(issued.UserCode, Guid.NewGuid(), approve: true).Should().Be(DevicePairingDecision.NotFound);
    }

    [Fact]
    public void An_approval_is_handed_to_the_device_once_and_names_the_account_that_gave_it()
    {
        var account = Guid.NewGuid();
        var issued = _store.Create(Tv);

        _store.Decide(issued.UserCode, account, approve: true).Should().Be(DevicePairingDecision.Approved);
        _clock.Advance(TimeSpan.FromSeconds(5));
        var polled = _store.Poll(issued.DeviceCode);
        _clock.Advance(TimeSpan.FromSeconds(5));
        var again = _store.Poll(issued.DeviceCode);

        polled.Status.Should().Be(DevicePairingPollStatus.Approved);
        polled.ApprovedBy.Should().Be(account);
        polled.Device.Should().Be(Tv);
        again.Status.Should().Be(DevicePairingPollStatus.Expired);
    }

    [Fact]
    public void A_denial_is_reported_once_and_gives_the_device_nothing()
    {
        var issued = _store.Create(Tv);

        _store.Decide(issued.UserCode, Guid.NewGuid(), approve: false).Should().Be(DevicePairingDecision.Denied);
        var polled = _store.Poll(issued.DeviceCode);

        polled.Status.Should().Be(DevicePairingPollStatus.Denied);
        polled.ApprovedBy.Should().BeNull();
        _clock.Advance(TimeSpan.FromSeconds(5));
        _store.Poll(issued.DeviceCode).Status.Should().Be(DevicePairingPollStatus.Expired);
    }

    [Fact]
    public void A_pairing_that_was_answered_cannot_be_answered_again()
    {
        var issued = _store.Create(Tv);
        _store.Decide(issued.UserCode, Guid.NewGuid(), approve: false);

        _store.Decide(issued.UserCode, Guid.NewGuid(), approve: true).Should().Be(DevicePairingDecision.AlreadyDecided);
        _store.FindPending(issued.UserCode).Should().BeNull();
    }

    [Fact]
    public void While_nobody_has_answered_polling_says_pending()
    {
        var issued = _store.Create(Tv);

        var polled = _store.Poll(issued.DeviceCode);

        polled.Status.Should().Be(DevicePairingPollStatus.Pending);
        polled.IntervalSeconds.Should().Be(5);
    }

    [Fact]
    public void A_device_that_polls_faster_than_the_interval_is_told_to_slow_down_and_its_interval_grows()
    {
        var issued = _store.Create(Tv);
        _store.Poll(issued.DeviceCode);

        _clock.Advance(TimeSpan.FromSeconds(2));
        var tooSoon = _store.Poll(issued.DeviceCode);
        _clock.Advance(TimeSpan.FromSeconds(6));
        var stillTooSoon = _store.Poll(issued.DeviceCode);
        _clock.Advance(TimeSpan.FromSeconds(15));
        var onTime = _store.Poll(issued.DeviceCode);

        tooSoon.Status.Should().Be(DevicePairingPollStatus.SlowDown);
        tooSoon.IntervalSeconds.Should().Be(10);
        stillTooSoon.Status.Should().Be(DevicePairingPollStatus.SlowDown);
        stillTooSoon.IntervalSeconds.Should().Be(15);
        onTime.Status.Should().Be(DevicePairingPollStatus.Pending);
    }

    [Fact]
    public void A_poll_a_little_early_is_forgiven_for_timer_jitter()
    {
        var issued = _store.Create(Tv);
        _store.Poll(issued.DeviceCode);

        _clock.Advance(TimeSpan.FromMilliseconds(4700));

        _store.Poll(issued.DeviceCode).Status.Should().Be(DevicePairingPollStatus.Pending);
    }

    [Fact]
    public void An_unknown_device_code_is_expired()
    {
        _store.Poll("not-a-code").Status.Should().Be(DevicePairingPollStatus.Expired);
        _store.Poll(string.Empty).Status.Should().Be(DevicePairingPollStatus.Expired);
    }

    [Fact]
    public void An_account_that_enters_five_wrong_codes_is_locked_out_for_ten_minutes()
    {
        var account = Guid.NewGuid();

        for (var i = 0; i < DevicePairingStore.MaxWrongCodes - 1; i++) _store.RecordWrongCode(account);
        _store.IsLockedOut(account).Should().BeFalse();
        _store.RecordWrongCode(account);

        _store.IsLockedOut(account).Should().BeTrue();
        _clock.Advance(TimeSpan.FromMinutes(9));
        _store.IsLockedOut(account).Should().BeTrue();
        _clock.Advance(TimeSpan.FromMinutes(2));
        _store.IsLockedOut(account).Should().BeFalse();
    }

    [Fact]
    public void Wrong_codes_are_counted_per_account_and_forgotten_once_a_code_is_right()
    {
        var account = Guid.NewGuid();
        var other = Guid.NewGuid();

        for (var i = 0; i < DevicePairingStore.MaxWrongCodes; i++) _store.RecordWrongCode(account);

        _store.IsLockedOut(other).Should().BeFalse();
        _store.ClearWrongCodes(account);
        _store.IsLockedOut(account).Should().BeFalse();
    }

    [Fact]
    public void Wrong_codes_older_than_the_window_stop_counting()
    {
        var account = Guid.NewGuid();
        for (var i = 0; i < DevicePairingStore.MaxWrongCodes - 1; i++) _store.RecordWrongCode(account);

        _clock.Advance(TimeSpan.FromMinutes(11));
        _store.RecordWrongCode(account);

        _store.IsLockedOut(account).Should().BeFalse();
    }
}
