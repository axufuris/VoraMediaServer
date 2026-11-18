using Microsoft.Extensions.Logging;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Auth.External;

public interface IExternalSignInService
{
    Task<ExternalSignInResult> SignInAsync(Guid providerId, ExternalClaims claims, ExternalSignInOptions options, CancellationToken cancellationToken = default);
    Task<ExternalSignInResult> LinkAccountAsync(Guid providerId, Guid userId, ExternalClaims claims, CancellationToken cancellationToken = default);
}

public sealed class ExternalSignInService(
    IAuthProviderRepository providers,
    IUserRepository users,
    ISystemSettingsRepository settingsRepo,
    IAuthManager authManager,
    IInvitationManager invitations,
    ILogger<ExternalSignInService> logger) : IExternalSignInService
{
    public async Task<ExternalSignInResult> SignInAsync(Guid providerId, ExternalClaims claims, ExternalSignInOptions options, CancellationToken cancellationToken = default)
    {
        var provider = await providers.GetByIdAsync(providerId);
        if (provider is null)
        {
            return ExternalSignInResult.Refused(ExternalSignInRefusal.ProviderUnavailable, "This sign-in provider is not available.");
        }

        var settings = await settingsRepo.GetSettingsAsync();
        var email = NormalizeEmail(claims.Email);
        var identity = await providers.GetIdentityAsync(provider.Id, claims.Subject);

        ExternalEmailMatch? emailMatch = null;
        var invite = ExternalInviteState.None;
        InvitationTicket? invitation = null;
        var pin = ExternalPinState.NotGiven;
        var credentials = ExternalCredentialState.NotGiven;
        Guid? credentialUserId = null;

        if (identity is null)
        {
            if (email is not null)
            {
                var match = await users.GetUserWithProfilesByEmailAsync(email);
                if (match is not null)
                {
                    emailMatch = new ExternalEmailMatch(match.Id, match.HasPassword, await providers.CountIdentitiesForUserAsync(match.Id) > 0);
                }
            }

            if (!string.IsNullOrWhiteSpace(options.InviteToken))
            {
                invitation = await invitations.ValidateTokenAsync(options.InviteToken);
                invite = invitation is null
                    ? ExternalInviteState.Invalid
                    : string.Equals(invitation.Email, email, StringComparison.OrdinalIgnoreCase) ? ExternalInviteState.Valid : ExternalInviteState.EmailMismatch;
            }

            if (!string.IsNullOrWhiteSpace(options.InvitePin))
            {
                pin = await FindPinTicketAsync(options.InvitePin) is null ? ExternalPinState.Invalid : ExternalPinState.Valid;
            }

            if (options.LinkTo is { } link)
            {
                credentialUserId = await authManager.VerifyCredentialsAsync(link.Email, link.Password);
                credentials = credentialUserId is null ? ExternalCredentialState.Rejected : ExternalCredentialState.Verified;
            }
        }

        var decision = ExternalSignInRules.Decide(new ExternalSignInFacts(
            provider.Enabled,
            provider.NewUserPolicy,
            provider.LinkByEmail,
            provider.AllowedEmailDomains,
            provider.RequiredGroup,
            settings.RegistrationMode,
            claims,
            identity is not null,
            emailMatch,
            invite,
            pin,
            credentials,
            credentialUserId,
            options.CreateAccount));

        switch (decision.Kind)
        {
            case ExternalDecisionKind.SignInLinked when identity is not null:
                return await SignInLinkedAsync(provider, identity, claims);

            case ExternalDecisionKind.LinkToEmailUser or ExternalDecisionKind.LinkToCredentialUser when decision.UserId is Guid userId:
                return await LinkAsync(provider, userId, claims);

            case ExternalDecisionKind.CreateAccount when email is not null:
                return await CreateAsync(provider, email, claims, decision.Proof, invitation, options.InvitePin);

            case ExternalDecisionKind.Respond when decision.Response is not null:
                return decision.Response;

            default:
                return ExternalSignInResult.Refused(ExternalSignInRefusal.ProviderUnavailable, "This sign-in could not be completed.");
        }
    }

    public async Task<ExternalSignInResult> LinkAccountAsync(Guid providerId, Guid userId, ExternalClaims claims, CancellationToken cancellationToken = default)
    {
        var provider = await providers.GetByIdAsync(providerId);
        if (provider is null)
        {
            return ExternalSignInResult.Refused(ExternalSignInRefusal.ProviderUnavailable, "This sign-in provider is not available.");
        }

        if (ExternalSignInRules.CheckAccess(provider.Enabled, provider.RequiredGroup, provider.AllowedEmailDomains, claims) is { } refused)
        {
            return refused;
        }

        var existing = await providers.GetIdentityAsync(provider.Id, claims.Subject);
        if (existing is not null)
        {
            return existing.UserId == userId
                ? ExternalSignInResult.SignedIn(userId, isNewAccount: false)
                : ExternalSignInResult.Refused(ExternalSignInRefusal.AlreadyLinked, "That provider account is already linked to another account on this server.");
        }

        if (await users.GetUserByIdAsync(userId) is null)
        {
            return ExternalSignInResult.Refused(ExternalSignInRefusal.ProviderUnavailable, "That account no longer exists.");
        }

        await providers.AddIdentityAsync(NewIdentity(provider, userId, claims));
        logger.LogInformation("Linked {Provider} to an account the person was signed in to", provider.Slug);
        return ExternalSignInResult.SignedIn(userId, isNewAccount: false);
    }

    private async Task<ExternalSignInResult> SignInLinkedAsync(AuthProvider provider, UserExternalIdentity identity, ExternalClaims claims)
    {
        var user = await users.GetUserByIdAsync(identity.UserId);
        if (user is null)
        {
            return ExternalSignInResult.Refused(ExternalSignInRefusal.ProviderUnavailable, "The account this sign-in was linked to no longer exists.");
        }

        identity.LastSignInAt = DateTime.UtcNow;
        identity.Email = NormalizeEmail(claims.Email) ?? identity.Email;
        await providers.UpdateIdentityAsync(identity);
        await PromoteAdminAsync(provider, user, claims);

        logger.LogInformation("Account {UserId} signed in through {Provider}", user.Id, provider.Slug);
        return ExternalSignInResult.SignedIn(user.Id, isNewAccount: false);
    }

    private async Task<ExternalSignInResult> LinkAsync(AuthProvider provider, Guid userId, ExternalClaims claims)
    {
        var user = await users.GetUserByIdAsync(userId);
        if (user is null)
        {
            return ExternalSignInResult.Refused(ExternalSignInRefusal.ProviderUnavailable, "That account no longer exists.");
        }

        await providers.AddIdentityAsync(NewIdentity(provider, user.Id, claims));
        await PromoteAdminAsync(provider, user, claims);

        logger.LogInformation("Linked {Provider} to account {UserId}", provider.Slug, user.Id);
        return ExternalSignInResult.SignedIn(user.Id, isNewAccount: false);
    }

    private async Task<ExternalSignInResult> CreateAsync(AuthProvider provider, string email, ExternalClaims claims, ExternalCreationProof proof, InvitationTicket? invitation, string? invitePin)
    {
        if (proof == ExternalCreationProof.InvitePin)
        {
            var ticket = invitePin is null ? null : await FindPinTicketAsync(invitePin);
            if (ticket is null)
            {
                return ExternalSignInResult.NeedsInvitePin(pinRejected: true);
            }

            await users.DeleteRegistrationTicketAsync(ticket);
        }
        else if (proof == ExternalCreationProof.InviteToken && invitation is not null)
        {
            await invitations.ConsumeAsync(invitation.TokenHash);
        }

        var user = await authManager.CreateExternalUserAsync(email, claims.DisplayName ?? string.Empty, ExternalSignInRules.GrantsAdmin(provider.AdminGroup, claims.Groups));
        await providers.AddIdentityAsync(NewIdentity(provider, user.Id, claims));

        logger.LogInformation("Created account {UserId} through {Provider}", user.Id, provider.Slug);
        return ExternalSignInResult.SignedIn(user.Id, isNewAccount: true);
    }

    private async Task PromoteAdminAsync(AuthProvider provider, User user, ExternalClaims claims)
    {
        if (user.IsAdmin || !ExternalSignInRules.GrantsAdmin(provider.AdminGroup, claims.Groups))
        {
            return;
        }

        user.IsAdmin = true;
        user.CanRequestMedia = true;
        user.CanTimeshiftIptv = true;
        user.CanAddCustomPodcastFeeds = true;
        user.CanRecordLiveTv = true;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await users.UpdateUserAsync(user);

        logger.LogInformation("Account {UserId} became an administrator through the {Provider} admin group", user.Id, provider.Slug);
    }

    private async Task<RegistrationTicket?> FindPinTicketAsync(string pin)
    {
        var ticket = await users.GetRegistrationTicketAsync(pin.Trim());
        return ticket is not null && ticket.ExpiresAt >= DateTime.UtcNow ? ticket : null;
    }

    private static UserExternalIdentity NewIdentity(AuthProvider provider, Guid userId, ExternalClaims claims) => new()
    {
        UserId = userId,
        ProviderId = provider.Id,
        Subject = claims.Subject,
        Email = NormalizeEmail(claims.Email),
        LinkedAt = DateTime.UtcNow,
        LastSignInAt = DateTime.UtcNow
    };

    private static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
}
