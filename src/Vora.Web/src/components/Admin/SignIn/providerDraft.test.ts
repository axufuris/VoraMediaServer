import { describe, it, expect } from 'vitest';
import { noDirectoryFields } from '../../../test/signInFixtures';
import type { AuthProviderAdmin } from '../../../api/Auth/signInProvidersAdminService';
import { blankDraft, draftFromProvider, hasBalancedBrackets, parseDomains, redirectUriFor, toSaveRequest, validateDraft } from './providerDraft';
import { LDAP_PRESETS } from './providerPresets';
import { savedDirectory } from '../../../test/signInFixtures';

const valid = (overrides = {}) => blankDraft({ displayName: 'Authentik', slug: 'authentik', issuer: 'https://auth.example.com/application/o/vora/', clientId: 'abc', ...overrides });

const saved: AuthProviderAdmin = {
    ...noDirectoryFields,
    id: 'p1', type: 'Oidc', slug: 'authentik', displayName: 'Authentik', enabled: true, iconUrl: null, sortOrder: 0,
    issuer: 'https://auth.example.com/application/o/vora/', clientId: 'abc', hasSecret: true, scopes: 'openid email profile groups',
    emailClaim: 'email', nameClaim: 'name', groupsClaim: 'groups', newUserPolicy: 'IdpDecides', linkByEmail: 'Automatic',
    allowedEmailDomains: ['example.com'], requiredGroup: 'vora', adminGroup: 'vora-admins', redirectUri: 'https://vora.example.com/api/auth/oidc/authentik/callback',
};

describe('validateDraft', () => {
    it('accepts a complete provider', () => {
        expect(validateDraft(valid({ enabled: true }))).toBeNull();
    });

    it('wants a name and a well-formed address name', () => {
        expect(validateDraft(valid({ displayName: '  ' }))).toMatch(/Enter a name/);
        expect(validateDraft(valid({ slug: 'Bad Name' }))).toMatch(/address name/);
        expect(validateDraft(valid({ slug: '-edge' }))).toMatch(/address name/);
    });

    it('refuses an issuer that is not a web address or still holds a placeholder', () => {
        expect(validateDraft(valid({ issuer: 'auth.example.com' }))).toMatch(/web address/);
        expect(validateDraft(valid({ issuer: 'https://login.microsoftonline.com/<tenant-id>/v2.0' }))).toMatch(/placeholder/);
        expect(validateDraft(valid({ issuer: 'https://auth.example.com/?x=1' }))).toMatch(/web address/);
    });

    it('needs the issuer and client id only to turn a provider on', () => {
        expect(validateDraft(valid({ issuer: '', clientId: '' }))).toBeNull();
        expect(validateDraft(valid({ issuer: '', enabled: true }))).toMatch(/before it can be turned on/);
        expect(validateDraft(valid({ clientId: '', enabled: true }))).toMatch(/before it can be turned on/);
    });

    it('requires openid in the scopes', () => {
        expect(validateDraft(valid({ scopes: 'email profile' }))).toMatch(/openid/);
        expect(validateDraft(valid({ scopes: 'openid,email' }))).toBeNull();
    });

    it('needs a group name when the administrator mapping is on', () => {
        expect(validateDraft(valid({ adminGroupOn: true, adminGroup: ' ' }))).toMatch(/administrators/);
        expect(validateDraft(valid({ adminGroupOn: true, adminGroup: 'vora-admins' }))).toBeNull();
    });
});

describe('toSaveRequest', () => {
    it('sends no secret when the field is left empty, so a saved one is kept', () => {
        const request = toSaveRequest(draftFromProvider(saved));

        expect('clientSecret' in request).toBe(false);
        expect('clearClientSecret' in request).toBe(false);
    });

    it('sends a typed secret and never asks to clear it at the same time', () => {
        const request = toSaveRequest({ ...draftFromProvider(saved), clientSecret: ' new-secret ', clearSecret: true });

        expect(request.clientSecret).toBe('new-secret');
        expect('clearClientSecret' in request).toBe(false);
    });

    it('asks to remove the saved secret only when the box is ticked and nothing new is typed', () => {
        expect(toSaveRequest({ ...draftFromProvider(saved), clearSecret: true }).clearClientSecret).toBe(true);
    });

    it('keeps the administrator group off unless it is turned on', () => {
        expect(toSaveRequest({ ...draftFromProvider(saved), adminGroupOn: false }).adminGroup).toBeNull();
        expect(toSaveRequest(draftFromProvider(saved)).adminGroup).toBe('vora-admins');
        expect(toSaveRequest(valid()).adminGroup).toBeNull();
    });

    it('reads domains from a comma or space separated list, without @ and in lower case', () => {
        expect(parseDomains('@Example.com, example.org  EXAMPLE.com;corp.test')).toEqual(['example.com', 'example.org', 'corp.test']);
    });

    it('normalises the address name and scopes', () => {
        const request = toSaveRequest(valid({ slug: ' Authentik ', scopes: 'openid, email  profile' }));

        expect(request.slug).toBe('authentik');
        expect(request.scopes).toBe('openid email profile');
        expect(request.type).toBe('Oidc');
    });
});

describe('redirectUriFor', () => {
    it('builds the callback from the public address and the address name', () => {
        expect(redirectUriFor('https://vora.example.com/', 'Authentik')).toBe('https://vora.example.com/api/auth/oidc/authentik/callback');
        expect(redirectUriFor('https://vora.example.com', '')).toBe('https://vora.example.com/api/auth/oidc/<address-name>/callback');
    });
});

describe('a new draft', () => {
    it('follows the registration mode, asks for the password once and keeps admin promotion off', () => {
        const draft = blankDraft();

        expect(draft.newUserPolicy).toBe('FollowRegistrationMode');
        expect(draft.linkByEmail).toBe('ConfirmWithPassword');
        expect(draft.adminGroupOn).toBe(false);
        expect(draft.enabled).toBe(false);
    });
});

const directory = (overrides = {}) => blankDraft({
    type: 'Ldap', displayName: 'Company directory', slug: 'directory', ldapHost: 'ldap.example.com',
    ldapUserBaseDn: 'ou=people,dc=example,dc=com', ldapUserFilter: '(&(objectClass=person)(uid={username}))', ...overrides,
});

describe('validateDraft for a directory', () => {
    it('accepts a complete directory and does not ask for an issuer or a client id', () => {
        expect(validateDraft(directory({ enabled: true }))).toBeNull();
    });

    it('still wants a name and a well-formed address name', () => {
        expect(validateDraft(directory({ displayName: ' ' }))).toMatch(/name of up to 64 characters for this directory/);
        expect(validateDraft(directory({ slug: 'Bad Name' }))).toMatch(/address name/);
    });

    it('needs the host, the search base and the filter only to turn a directory on', () => {
        expect(validateDraft(directory({ ldapHost: '', ldapUserBaseDn: '', ldapUserFilter: '' }))).toBeNull();
        for (const missing of [{ ldapHost: '' }, { ldapUserBaseDn: '' }, { ldapUserFilter: '' }]) {
            expect(validateDraft(directory({ enabled: true, ...missing }))).toMatch(/before it can be turned on/);
        }
    });

    it.each(['ldaps://ldap.example.com', 'ldap.example.com:636', 'ldap.example.com/ou', 'user@ldap.example.com', 'ldap example.com'])('refuses the host %s, which holds more than a name', host => {
        expect(validateDraft(directory({ ldapHost: host }))).toMatch(/only the directory host/);
    });

    it.each(['ldap.example.com', '192.168.1.20', 'fd00::1', 'directory'])('accepts the host %s', host => {
        expect(validateDraft(directory({ ldapHost: host }))).toBeNull();
    });

    it.each(['0', '65536', '12345678'])('refuses the port %s', port => {
        expect(validateDraft(directory({ ldapPort: port }))).toMatch(/port must be a number/);
    });

    it('wants {username} in a user filter that is closed properly', () => {
        expect(validateDraft(directory({ ldapUserFilter: '(uid=fixed)' }))).toMatch(/\{username\}/);
        expect(validateDraft(directory({ ldapUserFilter: 'uid={username}' }))).toMatch(/start with \(/);
        expect(validateDraft(directory({ ldapUserFilter: '(&(uid={username})' }))).toMatch(/start with \(/);
        expect(validateDraft(directory({ ldapUserFilter: '(|(uid={username})(mail={username}))' }))).toBeNull();
    });

    it('wants the group base and the group filter together, and {dn} or {username} in the filter', () => {
        expect(validateDraft(directory({ ldapGroupBaseDn: 'ou=groups,dc=example,dc=com' }))).toMatch(/both/);
        expect(validateDraft(directory({ ldapGroupFilter: '(member={dn})' }))).toMatch(/both/);
        expect(validateDraft(directory({ ldapGroupBaseDn: 'ou=groups,dc=example,dc=com', ldapGroupFilter: '(member=fixed)' }))).toMatch(/\{dn\}/);
        expect(validateDraft(directory({ ldapGroupBaseDn: 'ou=groups,dc=example,dc=com', ldapGroupFilter: '(memberUid={username})' }))).toBeNull();
        expect(validateDraft(directory({ ldapGroupBaseDn: 'ou=groups,dc=example,dc=com', ldapGroupFilter: '(&(objectClass=groupOfNames)(member={dn}))' }))).toBeNull();
    });

    it('refuses an attribute name that could change the search', () => {
        expect(validateDraft(directory({ ldapMailAttribute: 'mail)(uid=*' }))).toMatch(/mail attribute name/);
        expect(validateDraft(directory({ ldapUniqueIdAttribute: '' }))).toMatch(/unique id attribute name/);
        expect(validateDraft(directory({ ldapGroupAttribute: 'member Of' }))).toMatch(/group attribute name/);
        expect(validateDraft(directory({ ldapMailAttribute: 'mail;binary' }))).toBeNull();
    });

    it('wants a PEM certificate for the authority', () => {
        expect(validateDraft(directory({ ldapCaCertificatePem: 'not a certificate' }))).toMatch(/PEM certificate/);
        expect(validateDraft(directory({ ldapCaCertificatePem: '-----BEGIN CERTIFICATE-----\nAAAA\n-----END CERTIFICATE-----' }))).toBeNull();
    });

    it('makes plain ldap an explicit choice', () => {
        expect(validateDraft(directory({ ldapSecurity: 'None' }))).toMatch(/Tick the box/);
        expect(validateDraft(directory({ ldapSecurity: 'None', plainAcknowledged: true }))).toBeNull();
        expect(validateDraft(directory({ ldapSecurity: 'StartTls' }))).toBeNull();
    });

    it('treats every preset as a valid start once a host is filled in', () => {
        for (const preset of LDAP_PRESETS) {
            expect(validateDraft(blankDraft({ ...preset.draft, ldapHost: 'ldap.example.com', ldapUserBaseDn: 'dc=example,dc=com', enabled: true }))).toBeNull();
            expect(preset.draft.ldapUserFilter).toContain('{username}');
        }
    });
});

describe('toSaveRequest for a directory', () => {
    it('sends the directory fields and none of the OpenID Connect ones', () => {
        const request = toSaveRequest(directory({ ldapPort: ' 6360 ', ldapBindDn: ' cn=vora,dc=example,dc=com ', ldapMailAttribute: ' mail ' }), 3);

        expect(request).toMatchObject({
            type: 'Ldap', slug: 'directory', displayName: 'Company directory', sortOrder: 3, ldapHost: 'ldap.example.com', ldapPort: 6360,
            ldapSecurity: 'Ldaps', ldapBindDn: 'cn=vora,dc=example,dc=com', ldapUserBaseDn: 'ou=people,dc=example,dc=com',
            ldapUserFilter: '(&(objectClass=person)(uid={username}))', ldapMailAttribute: 'mail', ldapGroupBaseDn: null, ldapGroupFilter: null,
            newUserPolicy: 'FollowRegistrationMode', linkByEmail: 'ConfirmWithPassword', adminGroup: null,
        });
        for (const key of ['issuer', 'clientId', 'clientSecret', 'scopes', 'emailClaim']) {
            expect(key in request).toBe(false);
        }
    });

    it('leaves the port to the default when it is empty', () => {
        expect(toSaveRequest(directory()).ldapPort).toBeNull();
    });

    it('keeps a saved bind password when none is typed, replaces it when one is and removes it only on request', () => {
        const saved = draftFromProvider(savedDirectory);

        expect('ldapBindPassword' in toSaveRequest(saved)).toBe(false);
        expect('clearLdapBindPassword' in toSaveRequest(saved)).toBe(false);
        expect(toSaveRequest({ ...saved, ldapBindPassword: 'new-pw', clearBindPassword: true })).toMatchObject({ ldapBindPassword: 'new-pw' });
        expect('clearLdapBindPassword' in toSaveRequest({ ...saved, ldapBindPassword: 'new-pw', clearBindPassword: true })).toBe(false);
        expect(toSaveRequest({ ...saved, clearBindPassword: true }).clearLdapBindPassword).toBe(true);
    });

    it('does not carry an administrator group unless it is turned on', () => {
        expect(toSaveRequest(directory({ adminGroup: 'admins' })).adminGroup).toBeNull();
        expect(toSaveRequest(directory({ adminGroupOn: true, adminGroup: ' admins ' })).adminGroup).toBe('admins');
    });
});

describe('draftFromProvider for a directory', () => {
    it('reads back the fields, never a password, and treats a saved plain connection as acknowledged', () => {
        const draft = draftFromProvider({ ...savedDirectory, ldapSecurity: 'None', ldapPort: 389 });

        expect(draft).toMatchObject({ type: 'Ldap', ldapHost: 'ldap.example.com', ldapPort: '389', ldapSecurity: 'None', plainAcknowledged: true, ldapBindDn: 'cn=vora,ou=service,dc=example,dc=com' });
        expect(draft.ldapBindPassword).toBe('');
        expect(draftFromProvider(savedDirectory).plainAcknowledged).toBe(false);
    });
});

describe('hasBalancedBrackets', () => {
    it.each(['(uid=a)', '(&(a=1)(b=2))', '(|(uid={username})(mail={username}))', '(cn=a\\)b)'])('accepts %s', filter => {
        expect(hasBalancedBrackets(filter)).toBe(true);
    });

    it.each(['uid=a', '(uid=a', 'uid=a)', '(a=1)(b=2)', '((a=1)', '())('])('refuses %s', filter => {
        expect(hasBalancedBrackets(filter)).toBe(false);
    });
});
