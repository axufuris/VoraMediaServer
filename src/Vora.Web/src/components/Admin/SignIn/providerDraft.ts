import type { AuthProviderAdmin, LdapSecurity, LinkByEmailPolicy, NewUserPolicy, SaveAuthProvider } from '../../../api/Auth/signInProvidersAdminService';
import type { AuthProviderType } from '../../../api/Auth/externalSignInService';

export interface ProviderDraft {
    type: AuthProviderType;
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
    ldapHost: string;
    ldapPort: string;
    ldapSecurity: LdapSecurity;
    ldapCaCertificatePem: string;
    ldapBindDn: string;
    ldapBindPassword: string;
    clearBindPassword: boolean;
    ldapUserBaseDn: string;
    ldapUserFilter: string;
    ldapMailAttribute: string;
    ldapDisplayNameAttribute: string;
    ldapUniqueIdAttribute: string;
    ldapGroupAttribute: string;
    ldapGroupBaseDn: string;
    ldapGroupFilter: string;
    plainAcknowledged: boolean;
}

export const SLUG_PATTERN = /^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$/;
export const ATTRIBUTE_PATTERN = /^[A-Za-z][A-Za-z0-9-]*(?:;[A-Za-z0-9-]+)?$/;
export const DEFAULT_LDAP_PORTS: Record<LdapSecurity, number> = { Ldaps: 636, StartTls: 389, None: 389 };

export function blankDraft(overrides: Partial<ProviderDraft> = {}): ProviderDraft {
    return {
        type: 'Oidc',
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
        ldapHost: '',
        ldapPort: '',
        ldapSecurity: 'Ldaps',
        ldapCaCertificatePem: '',
        ldapBindDn: '',
        ldapBindPassword: '',
        clearBindPassword: false,
        ldapUserBaseDn: '',
        ldapUserFilter: '',
        ldapMailAttribute: 'mail',
        ldapDisplayNameAttribute: 'displayName',
        ldapUniqueIdAttribute: 'entryUUID',
        ldapGroupAttribute: 'memberOf',
        ldapGroupBaseDn: '',
        ldapGroupFilter: '',
        plainAcknowledged: false,
        ...overrides,
    };
}

export function draftFromProvider(provider: AuthProviderAdmin): ProviderDraft {
    return blankDraft({
        type: provider.type,
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
        ldapHost: provider.ldapHost ?? '',
        ldapPort: provider.ldapPort ? String(provider.ldapPort) : '',
        ldapSecurity: provider.ldapSecurity,
        ldapCaCertificatePem: provider.ldapCaCertificatePem ?? '',
        ldapBindDn: provider.ldapBindDn ?? '',
        ldapUserBaseDn: provider.ldapUserBaseDn ?? '',
        ldapUserFilter: provider.ldapUserFilter ?? '',
        ldapMailAttribute: provider.ldapMailAttribute || 'mail',
        ldapDisplayNameAttribute: provider.ldapDisplayNameAttribute || 'displayName',
        ldapUniqueIdAttribute: provider.ldapUniqueIdAttribute || 'entryUUID',
        ldapGroupAttribute: provider.ldapGroupAttribute || 'memberOf',
        ldapGroupBaseDn: provider.ldapGroupBaseDn ?? '',
        ldapGroupFilter: provider.ldapGroupFilter ?? '',
        plainAcknowledged: provider.ldapSecurity === 'None',
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

function validateCommon(draft: ProviderDraft): string | null {
    const name = draft.displayName.trim();
    if (name.length === 0 || name.length > 64) {
        return draft.type === 'Ldap'
            ? 'Enter a name of up to 64 characters for this directory, such as Company directory.'
            : 'Enter a name of up to 64 characters for the sign-in button.';
    }

    if (!SLUG_PATTERN.test(draft.slug.trim().toLowerCase())) {
        return 'The address name may use lower-case letters, digits and hyphens, and must start and end with a letter or digit.';
    }

    const icon = draft.iconUrl.trim();
    if (icon && !isHttpUrl(icon) && !icon.startsWith('/')) return 'The icon must be a web address or a path on this server.';

    if (draft.adminGroupOn && !draft.adminGroup.trim()) return 'Enter the group whose members become administrators, or turn that off.';

    return null;
}

function validateOidc(draft: ProviderDraft): string | null {
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

    return null;
}

/** A search filter that opens and closes its brackets in order, ignoring ones escaped with a backslash. */
export function hasBalancedBrackets(filter: string): boolean {
    if (!filter.startsWith('(') || !filter.endsWith(')')) return false;
    let depth = 0;
    for (let i = 0; i < filter.length; i++) {
        const ch = filter[i];
        if (ch === '\\') {
            i++;
        } else if (ch === '(') {
            depth++;
        } else if (ch === ')') {
            depth--;
            if (depth < 0) return false;
            if (depth === 0 && i < filter.length - 1) return false;
        }
    }
    return depth === 0;
}

function validateLdap(draft: ProviderDraft): string | null {
    const host = draft.ldapHost.trim();
    if (host && (/[\s/\\@?#]/.test(host) || host.includes('://') || /^[^:]+:\d+$/.test(host))) {
        return 'Enter only the directory host name or address, without ldap://, a path or a port. The port has its own box.';
    }

    const port = draft.ldapPort.trim();
    if (port && (!/^\d{1,5}$/.test(port) || Number(port) < 1 || Number(port) > 65535)) return 'The port must be a number from 1 to 65535.';

    const caPem = draft.ldapCaCertificatePem.trim();
    if (caPem && !/-----BEGIN CERTIFICATE-----[\s\S]+-----END CERTIFICATE-----/.test(caPem)) {
        return 'The certificate authority must be a PEM certificate, starting with -----BEGIN CERTIFICATE-----.';
    }

    if (draft.ldapSecurity === 'None' && !draft.plainAcknowledged) {
        return 'Without encryption every password crosses the network in the clear. Tick the box to confirm this is a network you trust, or choose ldaps or StartTLS.';
    }

    const filter = draft.ldapUserFilter.trim();
    if (draft.enabled && (!host || !draft.ldapUserBaseDn.trim() || !filter)) {
        return 'A directory needs its host, the user search base and the user filter before it can be turned on.';
    }
    if (filter) {
        if (!hasBalancedBrackets(filter)) return 'The user filter must start with ( and end with ), with its brackets closed, such as (&(objectClass=person)(uid={username})).';
        if (!filter.includes('{username}')) return 'The user filter must contain {username}, where the name typed on the sign-in page goes.';
    }

    const groupBase = draft.ldapGroupBaseDn.trim();
    const groupFilter = draft.ldapGroupFilter.trim();
    if (!!groupBase !== !!groupFilter) return 'Fill in both the group search base and the group filter, or leave both empty.';
    if (groupFilter) {
        if (!hasBalancedBrackets(groupFilter)) return 'The group filter must start with ( and end with ), with its brackets closed.';
        if (!groupFilter.includes('{dn}') && !groupFilter.includes('{username}')) return 'The group filter must contain {dn} (the person\'s entry) or {username}.';
    }

    const attributes: [string, string][] = [
        ['mail', draft.ldapMailAttribute], ['display name', draft.ldapDisplayNameAttribute],
        ['unique id', draft.ldapUniqueIdAttribute], ['group', draft.ldapGroupAttribute],
    ];
    for (const [label, value] of attributes) {
        if (!ATTRIBUTE_PATTERN.test(value.trim())) return `The ${label} attribute name is not valid. Use letters, digits and hyphens, such as mail.`;
    }

    return null;
}

export function validateDraft(draft: ProviderDraft): string | null {
    return validateCommon(draft) ?? (draft.type === 'Ldap' ? validateLdap(draft) : validateOidc(draft));
}

export function parseDomains(text: string): string[] {
    return Array.from(new Set(
        text.split(/[\s,;]+/).map(d => d.trim().replace(/^@/, '').toLowerCase()).filter(Boolean),
    ));
}

function toLdapRequest(draft: ProviderDraft): Partial<SaveAuthProvider> {
    const password = draft.ldapBindPassword;
    const port = draft.ldapPort.trim();
    return {
        ldapHost: draft.ldapHost.trim() || null,
        ldapPort: port ? Number(port) : null,
        ldapSecurity: draft.ldapSecurity,
        ldapCaCertificatePem: draft.ldapCaCertificatePem.trim() || null,
        ldapBindDn: draft.ldapBindDn.trim() || null,
        ...(password ? { ldapBindPassword: password } : {}),
        ...(!password && draft.clearBindPassword ? { clearLdapBindPassword: true } : {}),
        ldapUserBaseDn: draft.ldapUserBaseDn.trim() || null,
        ldapUserFilter: draft.ldapUserFilter.trim() || null,
        ldapMailAttribute: draft.ldapMailAttribute.trim(),
        ldapDisplayNameAttribute: draft.ldapDisplayNameAttribute.trim(),
        ldapUniqueIdAttribute: draft.ldapUniqueIdAttribute.trim(),
        ldapGroupAttribute: draft.ldapGroupAttribute.trim(),
        ldapGroupBaseDn: draft.ldapGroupBaseDn.trim() || null,
        ldapGroupFilter: draft.ldapGroupFilter.trim() || null,
    };
}

function toOidcRequest(draft: ProviderDraft): Partial<SaveAuthProvider> {
    const secret = draft.clientSecret.trim();
    return {
        issuer: draft.issuer.trim() || null,
        clientId: draft.clientId.trim() || null,
        ...(secret ? { clientSecret: secret } : {}),
        ...(!secret && draft.clearSecret ? { clearClientSecret: true } : {}),
        scopes: draft.scopes.split(/[\s,]+/).filter(Boolean).join(' '),
        emailClaim: draft.emailClaim.trim(),
        nameClaim: draft.nameClaim.trim(),
        groupsClaim: draft.groupsClaim.trim(),
    };
}

export function toSaveRequest(draft: ProviderDraft, sortOrder = 0): SaveAuthProvider {
    return {
        type: draft.type,
        slug: draft.slug.trim().toLowerCase(),
        displayName: draft.displayName.trim(),
        enabled: draft.enabled,
        iconUrl: draft.iconUrl.trim() || null,
        sortOrder,
        newUserPolicy: draft.newUserPolicy,
        linkByEmail: draft.linkByEmail,
        allowedEmailDomains: parseDomains(draft.allowedEmailDomains),
        requiredGroup: draft.requiredGroup.trim() || null,
        adminGroup: draft.adminGroupOn ? draft.adminGroup.trim() || null : null,
        ...(draft.type === 'Ldap' ? toLdapRequest(draft) : toOidcRequest(draft)),
    };
}

export function redirectUriFor(effectiveBaseUrl: string, slug: string): string {
    const base = effectiveBaseUrl.replace(/\/+$/, '');
    return `${base}/api/auth/oidc/${slug.trim().toLowerCase() || '<address-name>'}/callback`;
}
