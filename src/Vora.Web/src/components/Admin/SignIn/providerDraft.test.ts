import { describe, it, expect } from 'vitest';
import type { AuthProviderAdmin } from '../../../api/Auth/signInProvidersAdminService';
import { blankDraft, draftFromProvider, parseDomains, redirectUriFor, toSaveRequest, validateDraft } from './providerDraft';

const valid = (overrides = {}) => blankDraft({ displayName: 'Authentik', slug: 'authentik', issuer: 'https://auth.example.com/application/o/vora/', clientId: 'abc', ...overrides });

const saved: AuthProviderAdmin = {
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
