using Vora.Domain.Enums;

namespace Vora.Application.Auth.External;

public enum ExternalInviteState
{
    None,
    Valid,
    EmailMismatch,
    Invalid
}

public enum ExternalPinState
{
    NotGiven,
    Valid,
    Invalid
}

public enum ExternalCredentialState
{
    NotGiven,
    Verified,
    Rejected
}

public sealed record ExternalEmailMatch(Guid UserId, bool HasPassword, bool HasExternalIdentities);

public sealed record ExternalSignInFacts(
    bool ProviderEnabled,
    AuthNewUserPolicy NewUserPolicy,
    AuthLinkByEmailPolicy LinkByEmail,
    IReadOnlyCollection<string> AllowedEmailDomains,
    string? RequiredGroup,
    RegistrationMode RegistrationMode,
    ExternalClaims Claims,
    bool HasLinkedIdentity,
    ExternalEmailMatch? EmailMatch,
    ExternalInviteState Invite,
    ExternalPinState Pin,
    ExternalCredentialState Credentials,
    Guid? CredentialUserId,
    bool WantsNewAccount);

public enum ExternalDecisionKind
{
    SignInLinked,
    LinkToEmailUser,
    LinkToCredentialUser,
    CreateAccount,
    Respond
}

public enum ExternalCreationProof
{
    None,
    InvitePin,
    InviteToken
}

public sealed record ExternalDecision(ExternalDecisionKind Kind, Guid? UserId, ExternalCreationProof Proof, ExternalSignInResult? Response)
{
    public static ExternalDecision SignIn() => new(ExternalDecisionKind.SignInLinked, null, ExternalCreationProof.None, null);
    public static ExternalDecision LinkEmail(Guid userId) => new(ExternalDecisionKind.LinkToEmailUser, userId, ExternalCreationProof.None, null);
    public static ExternalDecision LinkCredential(Guid userId) => new(ExternalDecisionKind.LinkToCredentialUser, userId, ExternalCreationProof.None, null);
    public static ExternalDecision Create(ExternalCreationProof proof) => new(ExternalDecisionKind.CreateAccount, null, proof, null);
    public static ExternalDecision Reply(ExternalSignInResult response) => new(ExternalDecisionKind.Respond, null, ExternalCreationProof.None, response);
}

public static class ExternalSignInRules
{
    public static ExternalDecision Decide(ExternalSignInFacts facts)
    {
        var claims = facts.Claims;
        var email = string.IsNullOrWhiteSpace(claims.Email) ? null : claims.Email.Trim();

        if (!facts.ProviderEnabled)
        {
            return Refuse(ExternalSignInRefusal.ProviderUnavailable, "This sign-in provider is not available.");
        }

        if (!string.IsNullOrWhiteSpace(facts.RequiredGroup) && !ContainsIgnoreCase(claims.Groups, facts.RequiredGroup))
        {
            return Refuse(ExternalSignInRefusal.GroupNotAllowed, "Your account is not in the group that may sign in to this server.");
        }

        if (facts.AllowedEmailDomains.Count > 0)
        {
            if (email is null)
            {
                return Refuse(ExternalSignInRefusal.EmailMissing, "The sign-in provider did not share an email address, which this server requires.");
            }

            if (!facts.AllowedEmailDomains.Any(domain => string.Equals(domain.Trim().TrimStart('@'), DomainOf(email), StringComparison.OrdinalIgnoreCase)))
            {
                return Refuse(ExternalSignInRefusal.EmailDomainNotAllowed, "Your email address is not from a domain that may sign in to this server.");
            }
        }

        if (facts.HasLinkedIdentity)
        {
            return ExternalDecision.SignIn();
        }

        if (facts.Credentials == ExternalCredentialState.Verified && facts.CredentialUserId is Guid credentialUserId)
        {
            return ExternalDecision.LinkCredential(credentialUserId);
        }

        var credentialsRejected = facts.Credentials == ExternalCredentialState.Rejected;

        if (email is not null && facts.EmailMatch is { } match)
        {
            return DecideForEmailMatch(facts, match, credentialsRejected);
        }

        return DecideForNewAccount(facts, email, credentialsRejected);
    }

    public static bool GrantsAdmin(string? adminGroup, IReadOnlyCollection<string> groups) =>
        !string.IsNullOrWhiteSpace(adminGroup) && ContainsIgnoreCase(groups, adminGroup);

    private static ExternalDecision DecideForEmailMatch(ExternalSignInFacts facts, ExternalEmailMatch match, bool credentialsRejected)
    {
        if (facts.LinkByEmail == AuthLinkByEmailPolicy.Never)
        {
            return Refuse(ExternalSignInRefusal.EmailInUse, "An account with that email address already exists. Sign in the way you did before, then link this provider from Account settings.");
        }

        if (!facts.Claims.EmailVerified)
        {
            return Refuse(ExternalSignInRefusal.EmailNotVerified, "The sign-in provider has not verified your email address, so this sign-in cannot be matched to the account that uses it.");
        }

        if (match.HasPassword)
        {
            return facts.LinkByEmail == AuthLinkByEmailPolicy.Automatic
                ? ExternalDecision.LinkEmail(match.UserId)
                : ExternalDecision.Reply(ExternalSignInResult.NeedsLinkConfirmation(credentialsRejected));
        }

        if (!match.HasExternalIdentities || facts.LinkByEmail == AuthLinkByEmailPolicy.Automatic)
        {
            return ExternalDecision.LinkEmail(match.UserId);
        }

        return Refuse(ExternalSignInRefusal.EmailInUse, "An account with that email address already exists and signs in another way. Sign in that way, then link this provider from Account settings.");
    }

    private static ExternalDecision DecideForNewAccount(ExternalSignInFacts facts, string? email, bool credentialsRejected)
    {
        var gate = CreationGate(facts, email);

        if (!facts.WantsNewAccount)
        {
            if (gate.Allowed && gate.Proof == ExternalCreationProof.InviteToken)
            {
                return ExternalDecision.Create(ExternalCreationProof.InviteToken);
            }

            return ExternalDecision.Reply(ExternalSignInResult.NeedsCreateOrLink(gate.Allowed, gate.Refusal, gate.Message, credentialsRejected));
        }

        if (!gate.Allowed)
        {
            return Refuse(gate.Refusal, gate.Message ?? "A new account cannot be created from this sign-in.");
        }

        if (gate.Proof == ExternalCreationProof.InvitePin)
        {
            return facts.Pin == ExternalPinState.Valid
                ? ExternalDecision.Create(ExternalCreationProof.InvitePin)
                : ExternalDecision.Reply(ExternalSignInResult.NeedsInvitePin(facts.Pin == ExternalPinState.Invalid));
        }

        return ExternalDecision.Create(gate.Proof);
    }

    private sealed record Gate(bool Allowed, ExternalCreationProof Proof, ExternalSignInRefusal Refusal, string? Message);

    private static Gate CreationGate(ExternalSignInFacts facts, string? email)
    {
        if (email is null)
        {
            return Blocked(ExternalSignInRefusal.EmailMissing, "The sign-in provider did not share an email address, which a new account needs.");
        }

        if (facts.NewUserPolicy == AuthNewUserPolicy.ExistingOnly)
        {
            return Blocked(ExternalSignInRefusal.RegistrationClosed, "This server does not create accounts from this sign-in provider. Ask the administrator to add you.");
        }

        if (facts.NewUserPolicy == AuthNewUserPolicy.IdpDecides)
        {
            return new Gate(true, ExternalCreationProof.None, ExternalSignInRefusal.None, null);
        }

        return facts.RegistrationMode switch
        {
            RegistrationMode.Simple => new Gate(true, ExternalCreationProof.None, ExternalSignInRefusal.None, null),
            RegistrationMode.SecretWord => new Gate(true, ExternalCreationProof.InvitePin, ExternalSignInRefusal.None, null),
            RegistrationMode.Invitation => InvitationGate(facts),
            _ => Blocked(ExternalSignInRefusal.RegistrationClosed, "Registration is currently disabled by the server administrator.")
        };
    }

    private static Gate InvitationGate(ExternalSignInFacts facts) => facts.Invite switch
    {
        ExternalInviteState.Valid when facts.Claims.EmailVerified => new Gate(true, ExternalCreationProof.InviteToken, ExternalSignInRefusal.None, null),
        ExternalInviteState.Valid => Blocked(ExternalSignInRefusal.EmailNotVerified, "The sign-in provider has not verified your email address, so the invitation cannot be used with it."),
        ExternalInviteState.EmailMismatch => Blocked(ExternalSignInRefusal.InvitationMismatch, "The email address of this sign-in must match the address the invitation was sent to."),
        ExternalInviteState.Invalid => Blocked(ExternalSignInRefusal.InvitationRequired, "The invitation is invalid or has expired."),
        _ => Blocked(ExternalSignInRefusal.InvitationRequired, "Registration is invitation-only on this server. Ask the administrator to send you an email invitation.")
    };

    private static Gate Blocked(ExternalSignInRefusal refusal, string message) => new(false, ExternalCreationProof.None, refusal, message);

    private static ExternalDecision Refuse(ExternalSignInRefusal refusal, string message) =>
        ExternalDecision.Reply(ExternalSignInResult.Refused(refusal, message));

    private static bool ContainsIgnoreCase(IReadOnlyCollection<string> values, string wanted) =>
        values.Any(value => string.Equals(value, wanted, StringComparison.OrdinalIgnoreCase));

    private static string DomainOf(string email)
    {
        var at = email.LastIndexOf('@');
        return at < 0 ? string.Empty : email[(at + 1)..];
    }
}
