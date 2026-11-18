namespace Vora.Application.Auth.External;

public sealed record ExternalClaims(string Subject, string? Email, bool EmailVerified, string? DisplayName, IReadOnlyCollection<string> Groups);

public sealed record LocalCredentials(string Email, string Password);

public sealed record ExternalSignInOptions
{
    public string? InviteToken { get; init; }
    public string? InvitePin { get; init; }
    public bool CreateAccount { get; init; }
    public LocalCredentials? LinkTo { get; init; }
}

public enum ExternalSignInOutcome
{
    SignedIn,
    NeedsLinkConfirmation,
    NeedsInvitePin,
    NeedsCreateOrLink,
    Refused
}

public enum ExternalSignInRefusal
{
    None,
    ProviderUnavailable,
    EmailMissing,
    EmailNotVerified,
    EmailDomainNotAllowed,
    GroupNotAllowed,
    RegistrationClosed,
    InvitationRequired,
    InvitationMismatch,
    EmailInUse,
    AlreadyLinked
}

public sealed record ExternalSignInResult(
    ExternalSignInOutcome Outcome,
    Guid? UserId,
    ExternalSignInRefusal Refusal,
    string? Message,
    bool CanCreateAccount,
    bool PasswordRejected,
    bool InvitePinRejected,
    bool IsNewAccount)
{
    public static ExternalSignInResult SignedIn(Guid userId, bool isNewAccount) =>
        new(ExternalSignInOutcome.SignedIn, userId, ExternalSignInRefusal.None, null, false, false, false, isNewAccount);

    public static ExternalSignInResult Refused(ExternalSignInRefusal refusal, string message) =>
        new(ExternalSignInOutcome.Refused, null, refusal, message, false, false, false, false);

    public static ExternalSignInResult NeedsLinkConfirmation(bool passwordRejected) =>
        new(ExternalSignInOutcome.NeedsLinkConfirmation, null, ExternalSignInRefusal.None, null, false, passwordRejected, false, false);

    public static ExternalSignInResult NeedsInvitePin(bool pinRejected) =>
        new(ExternalSignInOutcome.NeedsInvitePin, null, ExternalSignInRefusal.None, null, true, false, pinRejected, false);

    public static ExternalSignInResult NeedsCreateOrLink(bool canCreate, ExternalSignInRefusal blockedBecause, string? message, bool passwordRejected) =>
        new(ExternalSignInOutcome.NeedsCreateOrLink, null, blockedBecause, message, canCreate, passwordRejected, false, false);
}
