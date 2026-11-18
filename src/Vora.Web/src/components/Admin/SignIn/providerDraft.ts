import type { AuthProviderAdmin, LinkByEmailPolicy, NewUserPolicy, SaveAuthProvider } from '../../../api/Auth/signInProvidersAdminService';

export interface ProviderDraft {
    displayName: string;
    slug: string;
    enabled: boolean;
    iconUrl: string;
    issuer: string;
    clientId: string;
    clientSecret: string;
    clearSecret: boolean;
    scopes: string;
    emailClaim: string;
    nameClaim: string;
    groupsClaim: string;
    newUserPolicy: NewUserPolicy;
    linkByEmail: LinkByEmailPolicy;
    allowedEmailDomains: string;
    requiredGroup: string;
    adminGroupOn: boolean;
    adminGroup: string;
}

export const SLUG_PATTERN = /^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$/;

export function blankDraft(overrides: Partial<ProviderDraft> = {}): ProviderDraft {
    return {
        displayName: '',
        slug: '',
        enabled: false,
        iconUrl: '',
        issuer: '',
        clientId: '',
        clientSecret: '',
        clearSecret: false,
        scopes: 'openid email profile',
        emailClaim: 'email',
        nameClaim: 'name',
        groupsClaim: 'groups',
        newUserPolicy: 'FollowRegistrationMode',
        linkByEmail: 'ConfirmWithPassword',
        allowedEmailDomains: '',
        requiredGroup: '',
        adminGroupOn: false,
        adminGroup: '',
        ...overrides,
    };
}

export function draftFromProvider(provider: AuthProviderAdmin): ProviderDraft {
    return blankDraft({
        displayName: provider.displayName,
        slug: provider.slug,
        enabled: provider.enabled,
        iconUrl: provider.iconUrl ?? '',
        issuer: provider.issuer ?? '',
        clientId: provider.clientId ?? '',
        scopes: provider.scopes,
        emailClaim: provider.emailClaim,
        nameClaim: provider.nameClaim,
        groupsClaim: provider.groupsClaim,
        newUserPolicy: provider.newUserPolicy,
        linkByEmail: provider.linkByEmail,
        allowedEmailDomains: provider.allowedEmailDomains.join(', '),
        requiredGroup: provider.requiredGroup ?? '',
        adminGroupOn: !!provider.adminGroup,
        adminGroup: provider.adminGroup ?? '',
    });
}

const isHttpUrl = (value: string): boolean => {
    try {
        const url = new URL(value);
        return url.protocol === 'http:' || url.protocol === 'https:';
    } catch {
        return false;
    }
};

export function validateDraft(draft: ProviderDraft): string | null {
    const name = draft.displayName.trim();
    if (name.length === 0 || name.length > 64) return 'Enter a name of up to 64 characters for the sign-in button.';

    if (!SLUG_PATTERN.test(draft.slug.trim().toLowerCase())) {
        return 'The address name may use lower-case letters, digits and hyphens, and must start and end with a letter or digit.';
    }

    const issuer = draft.issuer.trim();
    if (/[<>{}]/.test(issuer)) return 'Replace the placeholder in the issuer with your real address, such as your tenant id.';
    if (issuer && (!isHttpUrl(issuer) || issuer.includes('?') || issuer.includes('#'))) {
        return 'The issuer must be a web address such as https://auth.example.com/application/o/vora/.';
    }

    if (draft.enabled && (!issuer || !draft.clientId.trim())) {
        return 'A provider needs its issuer and client id before it can be turned on.';
    }

    const scopes = draft.scopes.split(/[\s,]+/).filter(Boolean);
    if (!scopes.includes('openid')) return 'The scopes must include openid.';

    const icon = draft.iconUrl.trim();
    if (icon && !isHttpUrl(icon) && !icon.startsWith('/')) return 'The icon must be a web address or a path on this server.';

    if (draft.adminGroupOn && !draft.adminGroup.trim()) return 'Enter the group whose members become administrators, or turn that off.';

    return null;
}

export function parseDomains(text: string): string[] {
    return Array.from(new Set(
        text.split(/[\s,;]+/).map(d => d.trim().replace(/^@/, '').toLowerCase()).filter(Boolean),
    ));
}

export function toSaveRequest(draft: ProviderDraft, sortOrder = 0): SaveAuthProvider {
    const secret = draft.clientSecret.trim();
    return {
        type: 'Oidc',
        slug: draft.slug.trim().toLowerCase(),
        displayName: draft.displayName.trim(),
        enabled: draft.enabled,
        iconUrl: draft.iconUrl.trim() || null,
        sortOrder,
        issuer: draft.issuer.trim() || null,
        clientId: draft.clientId.trim() || null,
        ...(secret ? { clientSecret: secret } : {}),
        ...(!secret && draft.clearSecret ? { clearClientSecret: true } : {}),
        scopes: draft.scopes.split(/[\s,]+/).filter(Boolean).join(' '),
        emailClaim: draft.emailClaim.trim(),
        nameClaim: draft.nameClaim.trim(),
        groupsClaim: draft.groupsClaim.trim(),
        newUserPolicy: draft.newUserPolicy,
        linkByEmail: draft.linkByEmail,
        allowedEmailDomains: parseDomains(draft.allowedEmailDomains),
        requiredGroup: draft.requiredGroup.trim() || null,
        adminGroup: draft.adminGroupOn ? draft.adminGroup.trim() || null : null,
    };
}

export function redirectUriFor(effectiveBaseUrl: string, slug: string): string {
    const base = effectiveBaseUrl.replace(/\/+$/, '');
    return `${base}/api/auth/oidc/${slug.trim().toLowerCase() || '<address-name>'}/callback`;
}
