import type { AuthProviderAdmin } from '../api/Auth/signInProvidersAdminService';

/** The directory fields of a provider that is not a directory, so OpenID Connect fixtures stay short. */
export const noDirectoryFields: Pick<AuthProviderAdmin,
    'ldapHost' | 'ldapPort' | 'ldapSecurity' | 'ldapCaCertificatePem' | 'ldapBindDn' | 'hasBindPassword' | 'ldapUserBaseDn' | 'ldapUserFilter'
    | 'ldapMailAttribute' | 'ldapDisplayNameAttribute' | 'ldapUniqueIdAttribute' | 'ldapGroupAttribute' | 'ldapGroupBaseDn' | 'ldapGroupFilter'> = {
    ldapHost: null,
    ldapPort: null,
    ldapSecurity: 'Ldaps',
    ldapCaCertificatePem: null,
    ldapBindDn: null,
    hasBindPassword: false,
    ldapUserBaseDn: null,
    ldapUserFilter: null,
    ldapMailAttribute: 'mail',
    ldapDisplayNameAttribute: 'displayName',
    ldapUniqueIdAttribute: 'entryUUID',
    ldapGroupAttribute: 'memberOf',
    ldapGroupBaseDn: null,
    ldapGroupFilter: null,
};

/** A saved directory, for the editor, the list and the test button. */
export const savedDirectory: AuthProviderAdmin = {
    ...noDirectoryFields,
    id: 'd1', type: 'Ldap', slug: 'directory', displayName: 'Company directory', enabled: true, iconUrl: null, sortOrder: 1,
    issuer: null, clientId: null, hasSecret: false, scopes: '', emailClaim: '', nameClaim: '', groupsClaim: '',
    newUserPolicy: 'FollowRegistrationMode', linkByEmail: 'ConfirmWithPassword', allowedEmailDomains: [], requiredGroup: null, adminGroup: null,
    redirectUri: 'https://vora.example.com/api/auth/oidc/directory/callback',
    ldapHost: 'ldap.example.com', ldapPort: null, ldapBindDn: 'cn=vora,ou=service,dc=example,dc=com', hasBindPassword: true,
    ldapUserBaseDn: 'ou=people,dc=example,dc=com', ldapUserFilter: '(&(objectClass=person)(uid={username}))',
};
