import { describe, it, expect, beforeEach } from 'vitest';
import { clearPostSignIn, peekPostSignIn, rememberPostSignIn } from './postSignInRedirect';
import { SessionKeys } from './storageKeys';

describe('postSignInRedirect', () => {
    beforeEach(() => sessionStorage.clear());

    it('hands back the page a visitor was sent away from', () => {
        rememberPostSignIn('/link?code=BCDFGHJK');

        expect(peekPostSignIn()).toBe('/link?code=BCDFGHJK');
    });

    it('keeps it until it is cleared', () => {
        rememberPostSignIn('/link');

        expect(peekPostSignIn()).toBe('/link');
        expect(peekPostSignIn()).toBe('/link');
        clearPostSignIn();
        expect(peekPostSignIn()).toBeNull();
    });

    it('forgets it after ten minutes, so a stale visit cannot hijack a later sign-in', () => {
        rememberPostSignIn('/link', 1_000);

        expect(peekPostSignIn(1_000 + 9 * 60 * 1000)).toBe('/link');
        expect(peekPostSignIn(1_000 + 11 * 60 * 1000)).toBeNull();
    });

    it.each(['https://evil.example/', '//evil.example/', 'javascript:alert(1)', 'link', '/\\evil'])('never remembers an address that leaves the app: %s', target => {
        rememberPostSignIn(target);

        expect(peekPostSignIn()).toBeNull();
    });

    it('ignores a stored value that is not what it wrote', () => {
        sessionStorage.setItem(SessionKeys.postSignInRedirect, '{"path":"https://evil.example/","savedAt":0}');
        expect(peekPostSignIn(1)).toBeNull();
        sessionStorage.setItem(SessionKeys.postSignInRedirect, 'not json');
        expect(peekPostSignIn()).toBeNull();
    });
});
