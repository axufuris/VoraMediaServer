using Vora.Application.Auth;
using Vora.Application.Auth.Dtos;
using Vora.Application.Auth.External;
using Vora.Application.Auth.External.Ldap;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Auth.Ldap;

public class PasswordSignInServiceTests
{
    private readonly IAuthManager _auth = Substitute.For<IAuthManager>();
    private readonly ILdapSignInManager _directory = Substitute.For<ILdapSignInManager>();
    private readonly PasswordSignInService _service;
    private readonly Guid _userId = Guid.NewGuid();

    public PasswordSignInServiceTests()
    {
        _service = new PasswordSignInService(_auth, _directory);
        _directory.AnyEnabledAsync().Returns(true);
        _auth.IsSignInLocked(Arg.Any<string>()).Returns(false);
    }

    private AuthResponseDto Response() => new() { AccessToken = "token", UserId = _userId, DisplayName = "Alice", IsAdmin = false };

    private void LocalFails() => _auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>()).Returns((AuthResponseDto?)null);

    [Fact]
    public async Task A_local_account_signs_in_without_the_directory_being_asked()
    {
        _auth.LoginAsync("sam@example.com", "pw").Returns(Response());

        var result = await _service.SignInAsync("sam@example.com", "pw", TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PasswordSignInOutcome.SignedIn);
        result.Method.Should().Be(AuthSignInMethod.Password);
        await _directory.DidNotReceiveWithAnyArgs().SignInAsync(default!, default!, default);
    }

    [Fact]
    public async Task A_locked_name_is_refused_before_anything_is_tried()
    {
        _auth.IsSignInLocked("alice").Returns(true);

        var result = await _service.SignInAsync("alice", "pw", TestContext.Current.CancellationToken);

        result.Should().BeSameAs(PasswordSignInResult.Invalid);
        await _auth.DidNotReceiveWithAnyArgs().LoginAsync(default!, default!);
        await _directory.DidNotReceiveWithAnyArgs().SignInAsync(default!, default!, default);
    }

    [Fact]
    public async Task When_no_local_account_matches_the_directory_is_tried_and_the_session_is_external()
    {
        LocalFails();
        _directory.SignInAsync("alice", "pw", Arg.Any<CancellationToken>()).Returns(LdapSignInResult.SignedIn(_userId));
        _auth.SignInExternalAsync(_userId).Returns(Response());

        var result = await _service.SignInAsync("alice", "pw", TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PasswordSignInOutcome.SignedIn);
        result.Method.Should().Be(AuthSignInMethod.External);
        result.Auth!.UserId.Should().Be(_userId);
        _auth.Received(1).ClearSignInFailures("alice");
    }

    [Fact]
    public async Task A_directory_sign_in_for_an_account_that_no_longer_exists_is_invalid()
    {
        LocalFails();
        _directory.SignInAsync("alice", "pw", Arg.Any<CancellationToken>()).Returns(LdapSignInResult.SignedIn(_userId));
        _auth.SignInExternalAsync(_userId).Returns((AuthResponseDto?)null);

        (await _service.SignInAsync("alice", "pw", TestContext.Current.CancellationToken)).Outcome.Should().Be(PasswordSignInOutcome.InvalidCredentials);
    }

    [Fact]
    public async Task Wrong_credentials_everywhere_are_invalid_and_the_failure_is_left_counted()
    {
        LocalFails();
        _directory.SignInAsync("alice", "wrong", Arg.Any<CancellationToken>()).Returns(LdapSignInResult.Invalid);

        var result = await _service.SignInAsync("alice", "wrong", TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PasswordSignInOutcome.InvalidCredentials);
        _auth.DidNotReceive().ClearSignInFailures(Arg.Any<string>());
    }

    [Fact]
    public async Task A_step_that_is_left_asks_the_web_to_finish_the_sign_in()
    {
        LocalFails();
        _directory.SignInAsync("alice", "pw", Arg.Any<CancellationToken>()).Returns(LdapSignInResult.Pending("handle-1", ExternalSignInOutcome.NeedsLinkConfirmation));

        var result = await _service.SignInAsync("alice", "pw", TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PasswordSignInOutcome.NeedsMore);
        result.PendingHandle.Should().Be("handle-1");
        result.PendingKind.Should().Be(ExternalSignInOutcome.NeedsLinkConfirmation);
        _auth.Received(1).ClearSignInFailures("alice");
    }

    [Fact]
    public async Task A_refusal_after_the_directory_accepted_the_password_is_said_and_does_not_count_as_a_failure()
    {
        LocalFails();
        _directory.SignInAsync("alice", "pw", Arg.Any<CancellationToken>()).Returns(LdapSignInResult.Refused("not in the group"));

        var result = await _service.SignInAsync("alice", "pw", TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PasswordSignInOutcome.Refused);
        result.Message.Should().Be("not in the group");
        _auth.Received(1).ClearSignInFailures("alice");
    }

    [Fact]
    public async Task Without_a_directory_a_wrong_password_is_invalid_and_nothing_else_is_asked()
    {
        _directory.AnyEnabledAsync().Returns(false);
        LocalFails();

        (await _service.SignInAsync("sam@example.com", "wrong", TestContext.Current.CancellationToken)).Outcome.Should().Be(PasswordSignInOutcome.InvalidCredentials);
        await _directory.DidNotReceiveWithAnyArgs().SignInAsync(default!, default!, default);
    }

    [Fact]
    public async Task With_password_sign_in_off_a_local_account_is_told_so_unless_the_directory_signs_it_in()
    {
        _auth.LoginAsync("sam@example.com", "pw").Returns<AuthResponseDto?>(_ => throw new PasswordSignInDisabledException());
        _directory.SignInAsync("sam@example.com", "pw", Arg.Any<CancellationToken>()).Returns(LdapSignInResult.Invalid);

        (await _service.SignInAsync("sam@example.com", "pw", TestContext.Current.CancellationToken)).Should().BeSameAs(PasswordSignInResult.Disabled);

        _directory.SignInAsync("sam@example.com", "pw", Arg.Any<CancellationToken>()).Returns(LdapSignInResult.SignedIn(_userId));
        _auth.SignInExternalAsync(_userId).Returns(Response());

        (await _service.SignInAsync("sam@example.com", "pw", TestContext.Current.CancellationToken)).Outcome.Should().Be(PasswordSignInOutcome.SignedIn);
    }

    [Fact]
    public async Task With_password_sign_in_off_and_no_directory_the_answer_is_the_switch()
    {
        _directory.AnyEnabledAsync().Returns(false);
        _auth.LoginAsync("sam@example.com", "pw").Returns<AuthResponseDto?>(_ => throw new PasswordSignInDisabledException());

        (await _service.SignInAsync("sam@example.com", "pw", TestContext.Current.CancellationToken)).Should().BeSameAs(PasswordSignInResult.Disabled);
    }
}
