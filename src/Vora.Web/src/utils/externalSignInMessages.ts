import { isAxiosError } from 'axios';
import type { ExternalRefusal } from '../api/Auth/externalSignInService';

const PROVIDER_ERRORS: Record<string, string> = {
    provider_unavailable: 'This sign-in option is not available right now. Try again later, or ask the server admin.',
    discovery_failed: "Vora couldn't reach the sign-in provider. Try again in a moment, or ask the server admin to check its settings.",
    access_denied: 'The sign-in was cancelled, or the provider did not allow it.',
    provider_error: 'The sign-in provider reported a problem. Try again, or ask the server admin.',
    state_expired: 'That sign-in took too long or was already used. Start again from the sign-in page.',
    token_exchange_failed: "The provider didn't accept this server's sign-in request. The server admin needs to check the client id and secret.",
    id_token_invalid: "The provider's answer could not be verified. Try again, or ask the server admin to check the provider's settings.",
    missing_subject: "The provider didn't say who you are, so you can't be signed in with it.",
};

const LINK_ERRORS: Record<string, string> = {
    link_conflict: 'That account is already linked to a different account on this server.',
    link_refused: "That account isn't allowed to sign in here (the provider's group or email domain limit).",
};

const FALLBACK = "That sign-in didn't finish. Try again from the sign-in page.";

export function externalErrorMessage(code: string | null): string {
    return (code && PROVIDER_ERRORS[code]) || FALLBACK;
}

export function linkErrorMessage(code: string | null): string {
    return (code && (LINK_ERRORS[code] ?? PROVIDER_ERRORS[code])) || "That account couldn't be linked. Try again from Account settings.";
}

const REFUSALS: Record<ExternalRefusal, string> = {
    None: FALLBACK,
    ProviderUnavailable: PROVIDER_ERRORS.provider_unavailable,
    EmailMissing: "The provider didn't send an email address, and this server needs one to make an account.",
    EmailNotVerified: "The provider hasn't verified your email address, so it can't be used here.",
    EmailDomainNotAllowed: "Your email address isn't one this server accepts from that provider.",
    GroupNotAllowed: "You aren't in the group this server requires for that provider.",
    RegistrationClosed: 'This server is not creating new accounts. Ask the server admin to add you.',
    InvitationRequired: 'This server needs an invitation to join. Open the link in your invitation email.',
    InvitationMismatch: "Your invitation was sent to a different email address than the one this provider gave.",
    EmailInUse: 'An account with that email already exists. Sign in the way you did before, then link this from Account settings.',
    AlreadyLinked: 'That sign-in is already linked to an account here.',
};

export function refusalMessage(refusal: ExternalRefusal, serverMessage: string | null): string {
    return serverMessage || REFUSALS[refusal] || FALLBACK;
}

/**
 * The handle of a sign-in the server wants finished on the sign-in page, or null.
 * The password login answers 409 with a "pending" extension when a directory accepted the
 * password but the account still has to be created, linked or unlocked with an invite PIN.
 */
export function pendingSignInHandle(err: unknown): string | null {
    if (!isAxiosError(err) || err.response?.status !== 409) return null;
    const data: unknown = err.response.data;
    if (data && typeof data === 'object' && 'pending' in data) {
        const { pending } = data as { pending?: unknown };
        if (typeof pending === 'string' && pending) return pending;
    }
    return null;
}
