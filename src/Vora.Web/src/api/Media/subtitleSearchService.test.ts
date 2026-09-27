import { describe, it, expect, afterEach, vi } from 'vitest';
import { defaultSubtitleLanguage, SUBTITLE_LANGUAGES } from './subtitleSearchService';

function withBrowserLanguage(value: string | undefined) {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue(value as string);
}

describe('default subtitle language', () => {
    afterEach(() => {
        vi.restoreAllMocks();
    });

    it.each([
        ['es', 'es'],
        ['fr-CA', 'fr'],
        ['PT-BR', 'pt'],
    ])('takes the base language from the browser preference %s', (browser, expected) => {
        withBrowserLanguage(browser);

        expect(defaultSubtitleLanguage()).toBe(expected);
    });

    // The picker can only display codes it carries, so a preference outside the
    // list has to fall back rather than select a value with no matching option —
    // which would render the dropdown blank.
    it.each(['cy', 'eu', 'xx-YY'])('falls back to English for %s, which the picker does not list', browser => {
        withBrowserLanguage(browser);

        expect(defaultSubtitleLanguage()).toBe('en');
    });

    it('falls back to English when the browser reports nothing', () => {
        withBrowserLanguage(undefined);

        expect(defaultSubtitleLanguage()).toBe('en');
    });

    it('always resolves to a language the picker offers', () => {
        withBrowserLanguage('de-AT');

        expect(SUBTITLE_LANGUAGES.map(l => l.code)).toContain(defaultSubtitleLanguage());
    });
});

describe('the offered languages', () => {
    it('has no duplicate codes', () => {
        const codes = SUBTITLE_LANGUAGES.map(l => l.code);

        expect(new Set(codes).size).toBe(codes.length);
    });

    it('offers English, since it is the fallback', () => {
        expect(SUBTITLE_LANGUAGES.some(l => l.code === 'en')).toBe(true);
    });
});
