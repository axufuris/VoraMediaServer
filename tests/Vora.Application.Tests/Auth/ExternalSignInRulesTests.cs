using Vora.Application.Auth.External;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Auth;

public class ExternalSignInRulesTests
{
    private static readonly Guid MatchedUser = Guid.NewGuid();

    private static ExternalSignInFacts Facts(
        AuthNewUserPolicy policy = AuthNewUserPolicy.FollowRegistrationMode,
        AuthLinkByEmailPolicy link = AuthLinkByEmailPolicy.ConfirmWithPassword,
        RegistrationMode mode = RegistrationMode.Simple,
        string? email = "sam@example.com",
        bool verified = true,
        string[]? groups = null,
        bool linked = false,
        ExternalEmailMatch? match = null,
        ExternalInviteState invite = ExternalInviteState.None,
        ExternalPinState pin = ExternalPinState.NotGiven,
        ExternalCredentialState credentials = ExternalCredentialState.NotGiven,
        Guid? credentialUser = null,
        bool wantsNew = false,
        bool enabled = true,
        string[]? domains = null,
        string? requiredGroup = null) =>
        new(
            enabled,
            policy,
            link,
            domains ?? [],
            requiredGroup,
            mode,
            new ExternalClaims("subject-1", email, verified, "Sam", groups ?? []),
            linked,
            match,
            invite,
            pin,
            credentials,
            credentialUser,
            wantsNew);

    private static string Describe(ExternalDecision decision)
    {
        if (decision.Kind != ExternalDecisionKind.Respond)
        {
            return decision.Kind == ExternalDecisionKind.CreateAccount ? $"Create:{decision.Proof}" : decision.Kind.ToString();
        }

        var response = decision.Response!;
        return response.Outcome switch
        {
            ExternalSignInOutcome.Refused => $"Refused:{response.Refusal}",
            ExternalSignInOutcome.NeedsCreateOrLink => $"NeedsCreateOrLink:canCreate={response.CanCreateAccount}:{response.Refusal}:passwordRejected={response.PasswordRejected}",
            ExternalSignInOutcome.NeedsInvitePin => $"NeedsInvitePin:rejected={response.InvitePinRejected}",
            ExternalSignInOutcome.NeedsLinkConfirmation => $"NeedsLinkConfirmation:passwordRejected={response.PasswordRejected}",
            _ => response.Outcome.ToString()
        };
    }

    [Fact]
    public void A_provider_that_is_turned_off_refuses_everyone_even_a_linked_account()
    {
        Describe(ExternalSignInRules.Decide(Facts(enabled: false, linked: true))).Should().Be("Refused:ProviderUnavailable");
    }

    [Theory]
    [InlineData(new string[0], "Refused:GroupNotAllowed")]
    [InlineData(new[] { "family" }, "Refused:GroupNotAllowed")]
    [InlineData(new[] { "Vora-Users" }, "SignInLinked")]
    [InlineData(new[] { "other", "vora-users" }, "SignInLinked")]
    public void A_required_group_gates_every_sign_in_including_linked_accounts(string[] groups, string expected)
    {
        Describe(ExternalSignInRules.Decide(Facts(groups: groups, requiredGroup: "vora-users", linked: true))).Should().Be(expected);
    }

    [Theory]
    [InlineData("sam@example.com", "Create:None")]
    [InlineData("Sam@Example.COM", "Create:None")]
    [InlineData("sam@other.org", "Refused:EmailDomainNotAllowed")]
    [InlineData("sam@sub.example.com", "Refused:EmailDomainNotAllowed")]
    [InlineData(null, "Refused:EmailMissing")]
    public void Allowed_email_domains_gate_every_sign_in(string? email, string expected)
    {
        var facts = Facts(email: email, domains: ["@example.com"], wantsNew: true);

        Describe(ExternalSignInRules.Decide(facts)).Should().Be(expected);
    }

    [Theory]
    [InlineData(RegistrationMode.Disabled)]
    [InlineData(RegistrationMode.Invitation)]
    [InlineData(RegistrationMode.SecretWord)]
    public void A_linked_account_signs_in_whatever_the_registration_mode_and_even_with_an_unverified_email(RegistrationMode mode)
    {
        Describe(ExternalSignInRules.Decide(Facts(mode: mode, linked: true, verified: false, email: null))).Should().Be("SignInLinked");
    }

    [Theory]
    [InlineData(AuthLinkByEmailPolicy.Never, true, MatchKind.HasPassword, "Refused:EmailInUse")]
    [InlineData(AuthLinkByEmailPolicy.Never, true, MatchKind.PasswordlessUnlinked, "Refused:EmailInUse")]
    [InlineData(AuthLinkByEmailPolicy.Never, false, MatchKind.HasPassword, "Refused:EmailInUse")]
    [InlineData(AuthLinkByEmailPolicy.ConfirmWithPassword, false, MatchKind.HasPassword, "Refused:EmailNotVerified")]
    [InlineData(AuthLinkByEmailPolicy.ConfirmWithPassword, false, MatchKind.PasswordlessUnlinked, "Refused:EmailNotVerified")]
    [InlineData(AuthLinkByEmailPolicy.Automatic, false, MatchKind.HasPassword, "Refused:EmailNotVerified")]
    [InlineData(AuthLinkByEmailPolicy.Automatic, false, MatchKind.PasswordlessUnlinked, "Refused:EmailNotVerified")]
    [InlineData(AuthLinkByEmailPolicy.ConfirmWithPassword, true, MatchKind.HasPassword, "NeedsLinkConfirmation:passwordRejected=False")]
    [InlineData(AuthLinkByEmailPolicy.Automatic, true, MatchKind.HasPassword, "LinkToEmailUser")]
    [InlineData(AuthLinkByEmailPolicy.ConfirmWithPassword, true, MatchKind.PasswordlessUnlinked, "LinkToEmailUser")]
    [InlineData(AuthLinkByEmailPolicy.Automatic, true, MatchKind.PasswordlessUnlinked, "LinkToEmailUser")]
    [InlineData(AuthLinkByEmailPolicy.ConfirmWithPassword, true, MatchKind.PasswordlessLinked, "Refused:EmailInUse")]
    [InlineData(AuthLinkByEmailPolicy.Automatic, true, MatchKind.PasswordlessLinked, "LinkToEmailUser")]
    public void An_email_match_links_by_the_providers_rule_and_never_on_an_unverified_email(AuthLinkByEmailPolicy link, bool verified, MatchKind kind, string expected)
    {
        var facts = Facts(link: link, verified: verified, match: Match(kind));

        Describe(ExternalSignInRules.Decide(facts)).Should().Be(expected);
    }

    public enum MatchKind
    {
        HasPassword,
        PasswordlessUnlinked,
        PasswordlessLinked
    }

    private static ExternalEmailMatch Match(MatchKind kind) => kind switch
    {
        MatchKind.HasPassword => new ExternalEmailMatch(MatchedUser, HasPassword: true, HasExternalIdentities: false),
        MatchKind.PasswordlessUnlinked => new ExternalEmailMatch(MatchedUser, HasPassword: false, HasExternalIdentities: false),
        _ => new ExternalEmailMatch(MatchedUser, HasPassword: false, HasExternalIdentities: true)
    };

    [Theory]
    [InlineData(AuthLinkByEmailPolicy.Never, RegistrationMode.Disabled)]
    [InlineData(AuthLinkByEmailPolicy.ConfirmWithPassword, RegistrationMode.Invitation)]
    [InlineData(AuthLinkByEmailPolicy.Automatic, RegistrationMode.SecretWord)]
    public void Proving_a_local_password_links_the_account_whatever_the_policy_and_registration_mode(AuthLinkByEmailPolicy link, RegistrationMode mode)
    {
        var other = Guid.NewGuid();
        var facts = Facts(link: link, mode: mode, credentials: ExternalCredentialState.Verified, credentialUser: other, match: Match(MatchKind.HasPassword));

        var decision = ExternalSignInRules.Decide(facts);

        decision.Kind.Should().Be(ExternalDecisionKind.LinkToCredentialUser);
        decision.UserId.Should().Be(other);
    }

    [Fact]
    public void A_wrong_password_asks_again_for_a_matched_email_and_for_a_new_person()
    {
        Describe(ExternalSignInRules.Decide(Facts(credentials: ExternalCredentialState.Rejected, match: Match(MatchKind.HasPassword))))
            .Should().Be("NeedsLinkConfirmation:passwordRejected=True");
        Describe(ExternalSignInRules.Decide(Facts(credentials: ExternalCredentialState.Rejected)))
            .Should().Be("NeedsCreateOrLink:canCreate=True:None:passwordRejected=True");
    }

    [Theory]
    [InlineData(AuthNewUserPolicy.ExistingOnly, RegistrationMode.Disabled, false, "NeedsCreateOrLink:canCreate=False:RegistrationClosed:passwordRejected=False")]
    [InlineData(AuthNewUserPolicy.ExistingOnly, RegistrationMode.Simple, false, "NeedsCreateOrLink:canCreate=False:RegistrationClosed:passwordRejected=False")]
    [InlineData(AuthNewUserPolicy.ExistingOnly, RegistrationMode.SecretWord, true, "Refused:RegistrationClosed")]
    [InlineData(AuthNewUserPolicy.ExistingOnly, RegistrationMode.Invitation, true, "Refused:RegistrationClosed")]
    [InlineData(AuthNewUserPolicy.IdpDecides, RegistrationMode.Disabled, false, "NeedsCreateOrLink:canCreate=True:None:passwordRejected=False")]
    [InlineData(AuthNewUserPolicy.IdpDecides, RegistrationMode.Invitation, false, "NeedsCreateOrLink:canCreate=True:None:passwordRejected=False")]
    [InlineData(AuthNewUserPolicy.IdpDecides, RegistrationMode.Disabled, true, "Create:None")]
    [InlineData(AuthNewUserPolicy.IdpDecides, RegistrationMode.SecretWord, true, "Create:None")]
    [InlineData(AuthNewUserPolicy.IdpDecides, RegistrationMode.Invitation, true, "Create:None")]
    [InlineData(AuthNewUserPolicy.FollowRegistrationMode, RegistrationMode.Disabled, false, "NeedsCreateOrLink:canCreate=False:RegistrationClosed:passwordRejected=False")]
    [InlineData(AuthNewUserPolicy.FollowRegistrationMode, RegistrationMode.Disabled, true, "Refused:RegistrationClosed")]
    [InlineData(AuthNewUserPolicy.FollowRegistrationMode, RegistrationMode.Simple, false, "NeedsCreateOrLink:canCreate=True:None:passwordRejected=False")]
    [InlineData(AuthNewUserPolicy.FollowRegistrationMode, RegistrationMode.Simple, true, "Create:None")]
    [InlineData(AuthNewUserPolicy.FollowRegistrationMode, RegistrationMode.SecretWord, false, "NeedsCreateOrLink:canCreate=True:None:passwordRejected=False")]
    [InlineData(AuthNewUserPolicy.FollowRegistrationMode, RegistrationMode.SecretWord, true, "NeedsInvitePin:rejected=False")]
    [InlineData(AuthNewUserPolicy.FollowRegistrationMode, RegistrationMode.Invitation, false, "NeedsCreateOrLink:canCreate=False:InvitationRequired:passwordRejected=False")]
    [InlineData(AuthNewUserPolicy.FollowRegistrationMode, RegistrationMode.Invitation, true, "Refused:InvitationRequired")]
    public void A_person_with_no_account_is_handled_by_the_policy_and_the_registration_mode(AuthNewUserPolicy policy, RegistrationMode mode, bool wantsNew, string expected)
    {
        Describe(ExternalSignInRules.Decide(Facts(policy: policy, mode: mode, wantsNew: wantsNew))).Should().Be(expected);
    }

    [Theory]
    [InlineData(ExternalPinState.NotGiven, "NeedsInvitePin:rejected=False")]
    [InlineData(ExternalPinState.Invalid, "NeedsInvitePin:rejected=True")]
    [InlineData(ExternalPinState.Valid, "Create:InvitePin")]
    public void In_invite_PIN_mode_a_new_account_needs_a_valid_PIN(ExternalPinState pin, string expected)
    {
        Describe(ExternalSignInRules.Decide(Facts(mode: RegistrationMode.SecretWord, wantsNew: true, pin: pin))).Should().Be(expected);
    }

    [Theory]
    [InlineData(ExternalInviteState.Valid, true, false, "Create:InviteToken")]
    [InlineData(ExternalInviteState.Valid, true, true, "Create:InviteToken")]
    [InlineData(ExternalInviteState.Valid, false, true, "Refused:EmailNotVerified")]
    [InlineData(ExternalInviteState.EmailMismatch, true, true, "Refused:InvitationMismatch")]
    [InlineData(ExternalInviteState.Invalid, true, true, "Refused:InvitationRequired")]
    [InlineData(ExternalInviteState.None, true, true, "Refused:InvitationRequired")]
    public void In_invitation_mode_only_a_valid_invitation_for_the_verified_email_creates_an_account(ExternalInviteState invite, bool verified, bool wantsNew, string expected)
    {
        Describe(ExternalSignInRules.Decide(Facts(mode: RegistrationMode.Invitation, invite: invite, verified: verified, wantsNew: wantsNew))).Should().Be(expected);
    }

    [Fact]
    public void An_invited_person_is_not_asked_to_choose_between_creating_and_linking()
    {
        Describe(ExternalSignInRules.Decide(Facts(mode: RegistrationMode.Invitation, invite: ExternalInviteState.Valid, wantsNew: false))).Should().Be("Create:InviteToken");
    }

    [Fact]
    public void Without_an_email_a_new_account_cannot_be_made_but_linking_to_an_existing_one_is_offered()
    {
        Describe(ExternalSignInRules.Decide(Facts(email: null, wantsNew: false))).Should().Be("NeedsCreateOrLink:canCreate=False:EmailMissing:passwordRejected=False");
        Describe(ExternalSignInRules.Decide(Facts(email: null, wantsNew: true))).Should().Be("Refused:EmailMissing");
    }

    [Theory]
    [InlineData("vora-admins", new[] { "vora-admins" }, true)]
    [InlineData("Vora-Admins", new[] { "vora-admins" }, true)]
    [InlineData("vora-admins", new[] { "vora-users" }, false)]
    [InlineData(null, new[] { "vora-admins" }, false)]
    [InlineData("", new[] { "vora-admins" }, false)]
    public void Admin_is_granted_only_by_the_admin_group_the_administrator_set(string? adminGroup, string[] groups, bool expected)
    {
        ExternalSignInRules.GrantsAdmin(adminGroup, groups).Should().Be(expected);
    }
}
