import { describe, it, expect } from 'vitest';
import { signInMethods, signInMethodsLine } from './signInMethods';

describe('how a person signs in', () => {
    it('is a password for an ordinary account, and for an older server that does not say', () => {
        expect(signInMethods({ hasPassword: true, signInProviders: [] })).toEqual(['Password']);
        expect(signInMethods({})).toEqual(['Password']);
        expect(signInMethodsLine({})).toBe('Signs in with Password');
    });

    it('lists the providers beside the password', () => {
        expect(signInMethodsLine({ hasPassword: true, signInProviders: ['Authentik', 'Google'] })).toBe('Signs in with Password · Authentik · Google');
    });

    it('is only the providers for an account with no password', () => {
        expect(signInMethodsLine({ hasPassword: false, signInProviders: ['Authentik'] })).toBe('Signs in with Authentik');
    });

    it('says it is waiting when an account has neither a password nor a linked provider yet', () => {
        expect(signInMethodsLine({ hasPassword: false, signInProviders: [] })).toBe('Waiting for their first provider sign-in');
    });
});
