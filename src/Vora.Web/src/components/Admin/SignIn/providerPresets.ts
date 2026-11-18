import type { AuthProviderType } from '../../../api/Auth/externalSignInService';
import type { ProviderDraft } from './providerDraft';

export interface ProviderPreset {
    id: 'google' | 'microsoft' | 'generic' | 'ldap' | 'active-directory' | 'openldap';
    type: AuthProviderType;
    label: string;
    description: string;
    steps: string;
    draft: Partial<ProviderDraft>;
}

export const PROVIDER_PRESETS: ProviderPreset[] = [
    {
        id: 'generic',
        type: 'Oidc',
        label: 'OpenID Connect',
        description: 'Authentik, Keycloak, Authelia, Pocket ID, Zitadel or any OpenID Connect server.',
        steps: 'Create an OpenID Connect (OAuth) application there, add the redirect address shown below, then paste its issuer address, client id and secret here.',
        draft: { displayName: 'Single sign-on', slug: 'sso' },
    },
    {
        id: 'google',
        type: 'Oidc',
        label: 'Google',
        description: 'Sign in with a Google account.',
        steps: 'In Google Cloud Console, open APIs & Services, then Credentials, and create an OAuth client ID of type Web application. Add the redirect address shown below as an authorized redirect URI, then paste the client id and secret here.',
        draft: { displayName: 'Google', slug: 'google', issuer: 'https://accounts.google.com' },
    },
    {
        id: 'microsoft',
        type: 'Oidc',
        label: 'Microsoft',
        description: 'Sign in with a Microsoft Entra (work, school or personal) account.',
        steps: 'In the Microsoft Entra admin center, register an application, add a Web redirect URI with the address shown below, and create a client secret. Replace <tenant-id> in the issuer with your Directory (tenant) ID, then paste the application (client) id and secret here.',
        draft: { displayName: 'Microsoft', slug: 'microsoft', issuer: 'https://login.microsoftonline.com/<tenant-id>/v2.0' },
    },
    {
        id: 'ldap',
        type: 'Ldap',
        label: 'LDAP directory',
        description: 'Any LDAP server. People sign in on the usual form with their directory username and password.',
        steps: 'Create a read-only service account in the directory, then enter the directory\'s host, that account and where the people are. Vora searches for the person with the service account and checks the password by signing in as them. Every person needs an email address in the directory, because Vora makes an account from it.',
        draft: {
            type: 'Ldap',
            displayName: 'Company directory',
            slug: 'ldap',
            ldapSecurity: 'Ldaps',
            ldapUserFilter: '(&(objectClass=person)(|(uid={username})(mail={username})))',
        },
    },
    {
        id: 'active-directory',
        type: 'Ldap',
        label: 'Active Directory',
        description: 'A Windows domain. People sign in with their sAMAccountName or email.',
        steps: 'Use a service account that can read users, and the domain controller\'s LDAPS address (port 636). If the domain controller\'s certificate comes from your own authority, paste that authority\'s certificate below. The filter skips disabled accounts. Replace the dc= parts with your domain.',
        draft: {
            type: 'Ldap',
            displayName: 'Active Directory',
            slug: 'active-directory',
            ldapSecurity: 'Ldaps',
            ldapUserBaseDn: 'dc=corp,dc=example,dc=com',
            ldapUserFilter: '(&(objectCategory=person)(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2))(|(sAMAccountName={username})(userPrincipalName={username})(mail={username})))',
            ldapMailAttribute: 'mail',
            ldapDisplayNameAttribute: 'displayName',
            ldapUniqueIdAttribute: 'objectGUID',
            ldapGroupAttribute: 'memberOf',
        },
    },
    {
        id: 'openldap',
        type: 'Ldap',
        label: 'OpenLDAP, LLDAP, Authentik',
        description: 'OpenLDAP, LLDAP, the Authentik LDAP outpost, FreeIPA and other standards-based directories.',
        steps: 'Use a read-only service account and the directory\'s LDAPS address. LLDAP: unique id attribute uuid, port 6360. Authentik: filter on cn instead of uid. memberOf needs the memberOf overlay in OpenLDAP; without it, use the group search below. The docs have the exact values for each.',
        draft: {
            type: 'Ldap',
            displayName: 'Home directory',
            slug: 'directory',
            ldapSecurity: 'Ldaps',
            ldapUserBaseDn: 'ou=people,dc=example,dc=com',
            ldapUserFilter: '(&(objectClass=inetOrgPerson)(|(uid={username})(mail={username})))',
            ldapMailAttribute: 'mail',
            ldapDisplayNameAttribute: 'displayName',
            ldapUniqueIdAttribute: 'entryUUID',
            ldapGroupAttribute: 'memberOf',
        },
    },
];

export const OIDC_PRESETS = PROVIDER_PRESETS.filter(preset => preset.type === 'Oidc');
export const LDAP_PRESETS = PROVIDER_PRESETS.filter(preset => preset.type === 'Ldap');
